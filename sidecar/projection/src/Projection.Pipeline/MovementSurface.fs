namespace Projection.Pipeline

// LINT-ALLOW-FILE-MUTATION: function-local accumulators while parsing the JSON
//   projection-config DOM into the immutable typed record; the mutation is
//   sealed at each parse function's exit (mirrors Config.fs).

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
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
    /// A file-production target: the SSDT bundle (CREATE files + RefactorLog +
    /// pre/post-deploy + data scripts) is WRITTEN to `out`, for Octopus to apply.
    /// `readConn` (optional, env:/file:) is a live connection to the real target
    /// database — present so a bundle place can ALSO be a direct READ source
    /// (the reverse leg, `compare`) even though its WRITE path is the file bundle.
    /// Resolves the write/read tension: schema goes DOWN as files; data is read
    /// UP live. THE_CLI §4.1.
    | Bundle of out: string * readConn: ConnectionRef option
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

/// The capability CLASS of a target (DATABASE_ARCHETYPES.md §1/§4) — the bundle
/// of covarying dispositions the engine forks on, named ONCE. `FullRights` = the
/// on-prem schema+data home (DDL + IDENTITY_INSERT — verified 2026-06-15);
/// `ManagedDml` = the managed DML-only sink (the J5 profile: sink-mints, no DDL /
/// IDENTITY_INSERT / CREATE TABLE). Closed so every consumer (the disposition
/// selector, the resume-mechanism chooser, the gate set, the renderer) is TOTAL
/// over it — a new target class joins by ONE DU case, never a parallel hand-list
/// (the `ArtifactByKind` / `registered ⇔ executed` discipline applied to
/// capability; CONSTELLATION §9.8.9). It SUBSUMES `Grant` (which becomes a
/// derived projection — `Archetype.grant`) and stays orthogonal to `Rendition`.
[<RequireQualifiedAccess>]
type Archetype =
    | FullRights
    | ManagedDml

/// Where mid-run resume state lives (DATABASE_ARCHETYPES.md §1/§2.2). A
/// `FullRights` sink can host a sink-resident progress table (needs CREATE
/// TABLE — durable, queryable, no filename↔digest coupling); a `ManagedDml` sink
/// must journal off-box (the client-side NDJSON journal — the only option under a
/// no-DDL grant). Closed so the resume-mechanism chooser (Slice C) is total.
[<RequireQualifiedAccess>]
type ResumeKind =
    | SinkResidentTable
    | ClientJournal

/// How a fresh load wipes a sink (DATABASE_ARCHETYPES.md §1/§2.5). `Truncate` is
/// the ALTER-gated fast refresh (`FullRights`); `ChildFirstDelete` is the only
/// DML-legal path under a no-ALTER grant (`ManagedDml` — the 2·|rows| CDC-costed
/// path). Closed so the wipe chooser is total.
[<RequireQualifiedAccess>]
type WipeKind =
    | Truncate
    | ChildFirstDelete

/// What an `Archetype` EXPANDS to — the disposition defaults the pipeline reads
/// instead of re-deciding "is this DML-only?" from scattered checks
/// (DATABASE_ARCHETYPES.md §4). Each capability is its OWN flag, NOT a bundle
/// inferred from the label, because a real estate hands you a SPLIT target (the
/// on-prem `FullRights`-minus-DMV, observed 2026-06-15 — DATABASE_ARCHETYPES.md
/// §5): the survey (Slice B) can flip an individual probed flag without
/// re-classing the whole archetype. `CapabilityProfile.of` is the SINGLE
/// expansion site (total over `Archetype`); the disposition selector, resume
/// chooser, and gate set all read this record.
type CapabilityProfile =
    {
        /// The coarse refusal-gate facet this archetype derives (subsumes the
        /// hand-set `Grant`): `FullRights → SchemaAndData`, `ManagedDml → DataOnly`.
        Grant            : Grant
        /// The identity-disposition DEFAULT the structural classifier starts from
        /// (per-table overrides still apply — a `ReconciledByRule` user table, a
        /// composite-key refusal): `FullRights → PreservedFromSource` (write source
        /// keys; no capture/remap/FK-repoint), `ManagedDml → AssignedBySink`.
        IdentityDefault  : IdentityDisposition
        /// CREATE TABLE / ALTER permitted — schema deploy + a sink-resident
        /// progress table become available.
        DdlPermitted     : bool
        /// IDENTITY_INSERT permitted — `PreservedFromSource` is viable on an
        /// IDENTITY PK (write the source surrogate directly).
        IdentityInsert   : bool
        /// NOCHECK / disable-trigger fast lane (needs ALTER).
        ConstraintBypass : bool
        /// Where a mid-run resume checkpoint lives.
        ResumeCheckpoint : ResumeKind
        /// How a fresh load wipes the sink.
        WipeStrategy     : WipeKind
    }

[<RequireQualifiedAccess>]
module Archetype =

    /// The `Grant` an archetype derives — the coarse facet becomes a PROJECTION
    /// of the class (DATABASE_ARCHETYPES.md §4). Total over `Archetype`.
    let grant (a: Archetype) : Grant =
        match a with
        | Archetype.FullRights -> Grant.SchemaAndData
        | Archetype.ManagedDml -> Grant.DataOnly

    /// Infer the archetype a `Grant` implies — the inverse of `grant`, used to
    /// DEFAULT an undeclared archetype from the existing `grant` facet
    /// (`SchemaAndData → FullRights`, `DataOnly → ManagedDml`). The two 2-element
    /// sets are in bijection, so `grant ∘ ofGrant = id` AND `ofGrant ∘ grant = id`
    /// — the round-trip witness (`ArchetypeTests`).
    let ofGrant (g: Grant) : Archetype =
        match g with
        | Grant.SchemaAndData -> Archetype.FullRights
        | Grant.DataOnly      -> Archetype.ManagedDml

[<RequireQualifiedAccess>]
module CapabilityProfile =

    /// EXPAND an archetype to its disposition bundle — the SINGLE definition site
    /// (DATABASE_ARCHETYPES.md §1/§4). Total over `Archetype`. The confirmed
    /// verdicts are pinned by `CapabilityProfileTests`: on-prem `FullRights` =
    /// DDL + IDENTITY_INSERT + constraint-bypass + a sink-resident progress table
    /// + TRUNCATE, PreservedFromSource by default; cloud `ManagedDml` = the J5
    /// ledger (none of those — sink-mints, client journal, child-first DELETE).
    let ``of`` (a: Archetype) : CapabilityProfile =
        match a with
        | Archetype.FullRights ->
            { Grant            = Grant.SchemaAndData
              IdentityDefault  = IdentityDisposition.PreservedFromSource
              DdlPermitted     = true
              IdentityInsert   = true
              ConstraintBypass = true
              ResumeCheckpoint = ResumeKind.SinkResidentTable
              WipeStrategy     = WipeKind.Truncate }
        | Archetype.ManagedDml ->
            { Grant            = Grant.DataOnly
              IdentityDefault  = IdentityDisposition.AssignedBySink
              DdlPermitted     = false
              IdentityInsert   = false
              ConstraintBypass = false
              ResumeCheckpoint = ResumeKind.ClientJournal
              WipeStrategy     = WipeKind.ChildFirstDelete }

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
        /// The capability CLASS this place DECLARES (DATABASE_ARCHETYPES.md §4) —
        /// the bundle of dispositions the engine forks on, named once. `None` =
        /// undeclared (the established surface — every config to date). Stored
        /// declared-only (NOT eagerly inferred) so existing configs render
        /// byte-identically and `parse ∘ render = id` holds without parse-time
        /// inference diverging; consumers DEFAULT it from `Grant` via
        /// `Environment.effectiveArchetype`. Nothing branches on it until Slices
        /// B/C/S — Slice A is byte-identical by construction.
        Archetype : Archetype option
        /// M22 — opt OUT of the atomic schema-deploy envelope for this place
        /// (`"atomicDeploy": false`). `None` = the derived default (ON for a direct
        /// full-access sink, inert otherwise). A capability override, not a gate.
        AtomicDeploy : bool option
        /// M23 — the data-leg revert policy for this sink (`"revert": script|auto|
        /// off`). `None` = the `Script` default (emit the revert script, never
        /// auto-delete). Per-environment, overridable per run (`--auto-revert`).
        Revert : RevertPolicy option
    }

[<RequireQualifiedAccess>]
module Environment =

    /// The EFFECTIVE archetype of a place — its declared `Archetype`, else
    /// inferred from the `Grant` facet (`SchemaAndData → FullRights`,
    /// `DataOnly → ManagedDml`; no grant ⇒ `None`). The DEFAULTING the design
    /// names (DATABASE_ARCHETYPES.md §6.1) done LAZILY at read time, so the stored
    /// field stays declared-only (byte-identical render) while consumers still see
    /// a class for any grant-bearing place. This is what Slices B/C/S read.
    let effectiveArchetype (env: Environment) : Archetype option =
        match env.Archetype with
        | Some a -> Some a
        | None   -> env.Grant |> Option.map Archetype.ofGrant

// `FlowSource`, `Flow`, and `FlowRunOpts` live in MovementSpec.fs (the types
// file) — `Intent.Flow` carries them, so they precede it in compile order.

/// Data-portability — a named SLICE FLOW: the complete cross-environment
/// recipe `slice-run <name>` executes — extract the named `Slice` from the
/// `Source` and apply it to the `Target`. `Source`/`Target` are connection refs
/// (`env:<VAR>` / `file:<path>` / `live:<connStr>`); `Slice` names an entry in
/// the `slices` block. The whole recipe lives in projection.json (config-
/// primary); the CLI just names it.
type SliceFlowSpec =
    { Source : string
      Slice  : string
      Target : string }

/// The parsed `projection.json`: named environments (places) and flows
/// (source→target Move recipes), the default authored model (so a flow needs
/// no model path), plus a global defaults block.
/// CROSS_ENVIRONMENT_READINESS.md §4 — the `readiness` block: confirm a set of
/// environments resolve to one agreed shape (espace-safe, via OSSYS identity)
/// with their data conforming. `Schema` is the agreed source environment's name;
/// `Confirm` the environment names checked against it. A movement-vocabulary
/// citizen (rendered, so `parse ∘ render = id`); absent ⇒ `None`.
type ReadinessSpec =
    { Schema  : string
      Confirm : string list }

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
        /// THE_SYNTHETIC_DATA_DESIGN §11 — the synthetic-load policy block
        /// (`"synthetic": {…}`), the DECLARATIVE baseline a `from: synthetic`
        /// flow rests on (τ / preserve / synthesize / scale / seed). A movement
        /// -vocabulary citizen (rendered by `renderConfig`, so `parse ∘ render =
        /// id` covers it). Absent ⇒ `Config.defaultSyntheticSection` (the
        /// built-in `SyntheticConfig.defaultConfig` holds; byte-identical).
        Synthetic    : Config.SyntheticSection
        /// F7 (audit 2026-06-17) — the operator-blessed tightening relaxations
        /// (`tighteningRelaxations`: a `kind.column` string array; the
        /// relax-ALWAYS persistence). Formerly written ONLY by `RelaxationStore`'s
        /// surgical JSON merge, alongside the movement vocabulary but invisible to
        /// `renderConfig` — so a render-then-parse cycle DROPPED a blessing,
        /// breaking A44 (`parse ∘ render = id`) for this key. Now a first-class
        /// movement-vocabulary citizen: rendered when non-empty (omitted when `[]`,
        /// so a config with no blessings round-trips to no key), parsed back here.
        TighteningRelaxations : string list
        /// Data-portability — the named use-case SLICE definitions
        /// (`"slices": { "<name>": { version, roots, directives } }`). Each is a
        /// `SliceSpec` (roots + traversal directives), the config-primary home
        /// for the "use case" `slice-extract` runs. A movement-vocabulary
        /// citizen: rendered by `renderConfig` (each spec via `SliceCodec`,
        /// omitted when empty), so `parse ∘ render = id` covers it. Empty ⇒ no
        /// `slices` key (byte-identical).
        Slices       : Map<string, SliceSpec>
        /// Data-portability — named SLICE FLOWS (`"sliceFlows": { "<name>":
        /// { source, slice, target } }`): the complete extract→apply recipes
        /// `slice-run <name>` executes. A movement-vocabulary citizen (rendered,
        /// so `parse ∘ render = id`); empty ⇒ no key.
        SliceFlows   : Map<string, SliceFlowSpec>
        /// CROSS_ENVIRONMENT_READINESS.md §4 — the `readiness` block (the agreed
        /// shape + the environments to confirm against it). A movement-vocabulary
        /// citizen (rendered, so `parse ∘ render = id`); absent ⇒ `None`.
        Readiness    : ReadinessSpec option
        /// The file the config was loaded from (S6.2 — `fromFile` sets `Some
        /// path`; `parse`/`empty` set `None`). It is LOAD PROVENANCE, not a JSON
        /// field — `renderConfig` never emits it, so the `parse ∘ render` round
        /// trip ignores it. `resolveFlowSpec` emits `ModelSource.ConfigFile
        /// sourcePath` (firing the publish-with-provenance arms) only when this is
        /// `Some` and the sink carries a `store` and the model path is present;
        /// otherwise it stays `None` and the byte-identical ModelFile/Unspecified
        /// path holds.
        SourcePath   : string option
    }

[<RequireQualifiedAccess>]
module ProjectionConfig =

    let empty : ProjectionConfig =
        { Environments = Map.empty; Flows = Map.empty; Model = None; ModelOssys = None; Defaults = Map.empty
          Shaping = Config.defaultConfig; Synthetic = Config.defaultSyntheticSection
          TighteningRelaxations = []; Slices = Map.empty; SliceFlows = Map.empty; Readiness = None; SourcePath = None }

    let private err (code: string) (message: string) : ValidationError =
        ValidationError.create code message

    /// Reconstruct the out-of-band connection-spec text (`env:<VAR>` /
    /// `file:<path>`) from a resolved `ConnectionRef` — the dual of
    /// `TransferSpec.parseConnectionSpec`, hoisted above `parse` so `model.env`
    /// resolution can reach it. `renderConnRef` (the rendering dual) delegates
    /// here; `Command.connSpecOf` is the same total function in the sibling
    /// module (a separate compile scope can't share it without hoisting above
    /// both, out of scope for this slice). D9 holds by shape — never a secret.
    let private connRefToSpec (r: ConnectionRef) : string =
        match r with
        | ConnectionRef.EnvVar n -> "env:" + n
        | ConnectionRef.File p   -> "file:" + p
        // Raw never comes FROM config parsing (D9 belt: the config carries
        // references, never secrets); round-tripping one would re-embed the
        // secret, so it maps to the openable `live:` spec form instead.
        | ConnectionRef.Raw c    -> "live:" + c

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

    /// A string-array property → a trimmed, non-empty `string list` (absent ⇒ []).
    let private getStringArray (el: JsonElement) (name: string) : string list =
        match el.TryGetProperty name with
        | true, a when a.ValueKind = JsonValueKind.Array ->
            [ for v in a.EnumerateArray() do
                if v.ValueKind = JsonValueKind.String then
                    match Option.ofObj (v.GetString()) with
                    | Some s when not (String.IsNullOrWhiteSpace s) -> yield s.Trim()
                    | _ -> () ]
        | _ -> []

    /// THE_SYNTHETIC_DATA_DESIGN §11 — parse the top-level `"synthetic"` block to a
    /// `SyntheticSection` (the declarative baseline a `from: synthetic` flow rests
    /// on). Absent / non-object ⇒ the default (every knob None/[]). Lenient: a
    /// malformed number is simply absent (the CLI / built-in default fills it) —
    /// the block is an OVERRIDE layer, not a structural requirement.
    let private parseSynthetic (root: JsonElement) : Config.SyntheticSection =
        match root.TryGetProperty "synthetic" with
        | true, s when s.ValueKind = JsonValueKind.Object ->
            let num (name: string) : JsonElement option =
                match s.TryGetProperty name with
                | true, v when v.ValueKind = JsonValueKind.Number -> Some v
                | _ -> None
            let int64Of name   = num name |> Option.bind (fun (v: JsonElement) -> match v.TryGetInt64()   with true, n -> Some n | _ -> None)
            let decimalOf name = num name |> Option.bind (fun (v: JsonElement) -> match v.TryGetDecimal() with true, d -> Some d | _ -> None)
            let uint64Of name  = num name |> Option.bind (fun (v: JsonElement) -> match v.TryGetUInt64()  with true, n -> Some n | _ -> None)
            let boolOf (name: string) : bool =
                match s.TryGetProperty name with
                | true, v when v.ValueKind = JsonValueKind.True  -> true
                | _ -> false
            { PreserveCardinalityMax = int64Of "preserveCardinalityMax"
              Preserve               = getStringArray s "preserve"
              Synthesize             = getStringArray s "synthesize"
              Scale                  = decimalOf "scale"
              Seed                   = uint64Of "seed"
              WeightVolumeByCentrality = boolOf "weightVolumeByCentrality"
              ClusterFksByContext      = boolOf "clusterFksByContext" }
        | _ -> Config.defaultSyntheticSection

    /// Data-portability — parse the top-level `"slices"` block to the named
    /// `SliceSpec` map. Each named value is decoded (and RE-VALIDATED, A39)
    /// through `SliceCodec`; an invalid slice fails the whole config parse
    /// (caught by the outer `try` → `cli.config.parseFailed`), never a silent
    /// drop. Absent ⇒ the empty map (byte-identical; no round-trip key).
    let private parseSlices (root: JsonElement) : Map<string, SliceSpec> =
        match root.TryGetProperty "slices" with
        | true, slicesEl when slicesEl.ValueKind = JsonValueKind.Object ->
            slicesEl.EnumerateObject()
            |> Seq.map (fun p ->
                match Projection.Targets.Json.SliceCodec.deserialize (p.Value.GetRawText()) with
                | Ok spec  -> p.Name, spec
                | Error es ->
                    failwithf "slice '%s' is invalid: %s"
                        p.Name (es |> List.map (fun e -> e.Code) |> String.concat ", "))
            |> Map.ofSeq
        | _ -> Map.empty

    /// Data-portability — parse the `"sliceFlows"` block to the named
    /// `SliceFlowSpec` map (source ref + slice name + target ref). A flow
    /// missing a required field fails the config parse (outer `try` →
    /// `cli.config.parseFailed`). Absent ⇒ the empty map.
    let private parseSliceFlows (root: JsonElement) : Map<string, SliceFlowSpec> =
        match root.TryGetProperty "sliceFlows" with
        | true, flowsEl when flowsEl.ValueKind = JsonValueKind.Object ->
            flowsEl.EnumerateObject()
            |> Seq.map (fun p ->
                let req (field: string) =
                    match getString p.Value field with
                    | Some v -> v
                    | None   -> failwithf "sliceFlow '%s': missing required field '%s'" p.Name field
                p.Name, { Source = req "source"; Slice = req "slice"; Target = req "target" })
            |> Map.ofSeq
        | _ -> Map.empty

    /// CROSS_ENVIRONMENT_READINESS.md §4 — parse the `"readiness"` block. A
    /// present block names its agreed shape via `schema` (an environment), OR
    /// omits it to DEFAULT to `model.env` (`defaultSchema`) — the optional gate
    /// defers to the mandatory model source, so the canonical environment is
    /// named once. The `confirm` array (the environments to check) defaults to
    /// empty. A present block with neither an explicit `schema` nor a
    /// `model.env` to default from fails the config parse (outer `try` →
    /// `cli.config.parseFailed`), never a silent drop. Absent ⇒ `None` (no
    /// `readiness` key; byte-identical). On render the resolved `schema` is
    /// emitted explicitly, so `parse ∘ render` is stable at the config value.
    let private parseReadiness (defaultSchema: string option) (root: JsonElement) : ReadinessSpec option =
        match root.TryGetProperty "readiness" with
        | true, r when r.ValueKind = JsonValueKind.Object ->
            match getString r "schema", defaultSchema with
            | Some schema, _ -> Some { Schema = schema; Confirm = getStringArray r "confirm" }
            | None, Some def -> Some { Schema = def;    Confirm = getStringArray r "confirm" }
            | None, None ->
                failwith "readiness block sets no 'schema' and there is no model.env to default it from — name the agreed shape's environment (or set model.env)."
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
                | None -> Result.failureOf (err "cli.config.envBundleNoOut" (sprintf "environment '%s' is access:bundle but sets no 'out' folder." envName))
                | Some out ->
                    // A bundle place WRITES SSDT files to `out`; it MAY also carry a
                    // `conn` (env:/file:, D9) — a live READ connection so the real
                    // target database serves as a reverse-leg source / `compare`
                    // operand, even though its write path is the file bundle.
                    match getString el "conn" with
                    | None -> Result.success (Access.Bundle (out, None))
                    | Some conn ->
                        if looksLikeSecret conn then
                            Result.failureOf (err "cli.config.envSecretInline" (sprintf "environment '%s' conn looks like an inline secret; use env:<VAR> or file:<path> (D9)." envName))
                        else
                            match TransferSpec.parseConnectionSpec conn with
                            | Ok r    -> Result.success (Access.Bundle (out, Some r))
                            | Error e -> Result.failure e
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

    /// The capability-class disposition facet (DATABASE_ARCHETYPES.md §4):
    /// full-rights (on-prem, DDL+IDENTITY_INSERT) | managed-dml (cloud, the J5
    /// DML-only profile). Absent = `None` (consumers default it from `grant`).
    /// Closed; an unknown value is a named refusal, never silently dropped.
    let private parseArchetype (envName: string) (raw: string) : Result<Archetype> =
        match raw.ToLowerInvariant() with
        | "fullrights" | "full-rights" | "full"          -> Result.success Archetype.FullRights
        | "manageddml" | "managed-dml" | "managed" | "dml" -> Result.success Archetype.ManagedDml
        | other -> Result.failureOf (err "cli.config.envArchetypeUnknown" (sprintf "environment '%s' archetype '%s' is not full-rights | managed-dml." envName other))

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
                let archetypeR =
                    match getString el "archetype" with
                    | None   -> Result.success None
                    | Some a -> parseArchetype name a |> Result.map Some
                // M22 — `atomicDeploy` (JSON bool): the opt-out override for the
                // derived atomic schema deploy. `None` = derive from the archetype.
                let atomicDeploy =
                    match el.TryGetProperty "atomicDeploy" with
                    | true, v when v.ValueKind = JsonValueKind.True  -> Some true
                    | true, v when v.ValueKind = JsonValueKind.False -> Some false
                    | _ -> None
                // M23 — `revert` (script|auto|off): the per-environment data-leg
                // revert policy. Unknown token is a named refusal, never dropped.
                let revertR : Result<RevertPolicy option> =
                    match getString el "revert" with
                    | None   -> Result.success None
                    | Some r ->
                        match RevertPolicy.tryParse r with
                        | Ok p    -> Result.success (Some p)
                        | Error m -> Result.failureOf (err "cli.config.revert" (sprintf "environment '%s': %s." name m))
                match renditionR, archetypeR, revertR with
                | Error e, _, _ | _, Error e, _ | _, _, Error e -> Result.failure e
                | Ok rendition, Ok archetype, Ok revert ->
                    match getString el "grant" with
                    | None -> Result.success { Name = name; Access = access; Grant = None; Store = store; Rendition = rendition; Archetype = archetype; AtomicDeploy = atomicDeploy; Revert = revert }
                    | Some g ->
                        match parseGrant name g with
                        | Ok grant -> Result.success { Name = name; Access = access; Grant = Some grant; Store = store; Rendition = rendition; Archetype = archetype; AtomicDeploy = atomicDeploy; Revert = revert }
                        | Error e  -> Result.failure e

    /// The content origin: `from` names an environment (the Move), or one of
    /// the keywords `model` / `synthetic` (with optional `profile`) / `none`.
    let private parseFlowSource (el: JsonElement) : FlowSource =
        match getString el "from" with
        | None -> FlowSource.Model
        | Some f ->
            match f.ToLowerInvariant() with
            | "model"     -> FlowSource.Model
            | "synthetic" -> FlowSource.Synthetic (getString el "profile", getString el "correction")
            | "none"      -> FlowSource.NoData
            | _           -> FlowSource.Env f

    /// The move's PROJECTION (G1): an optional per-flow `"scope": "schema" |
    /// "data" | "both"` that decides which legs of the T16 square THIS move
    /// carries — decoupled from the target's `grant` (the refusal gate). Absent
    /// = `None` (the grant-derived default, resolved later). Closed; an unknown
    /// value is a NAMED refusal (`cli.config.flowScopeUnknown`), never silent.
    let private parseFlowScope (name: string) (el: JsonElement) : Result<Scope option> =
        match getString el "scope" with
        | None -> Result.success None
        | Some raw ->
            match raw.ToLowerInvariant() with
            | "schema"          -> Result.success (Some Scope.Schema)
            | "data"            -> Result.success (Some Scope.Data)
            | "both" | "all" | "schema+data" | "schemaanddata" | "schema-and-data" ->
                Result.success (Some Scope.All)
            | other ->
                Result.failureOf (err "cli.config.flowScopeUnknown" (sprintf "flow '%s' scope '%s' is not schema | data | both." name other))

    /// The bundle composition this move emits (S6.1): an optional per-flow
    /// `"shape": "bundle" | "ssdt" | "skeleton"`. Absent = `None` (the `Bundle`
    /// default, resolved later — so a folder model flow still emits the full
    /// pass-chain bundle by default; `skeleton` selects the pre-overlay emit).
    /// Closed; an unknown value is a NAMED refusal (`cli.config.flowShapeUnknown`),
    /// never silent (mirrors `parseFlowScope`).
    let private parseFlowShape (name: string) (el: JsonElement) : Result<Shape option> =
        match getString el "shape" with
        | None -> Result.success None
        | Some raw ->
            match raw.ToLowerInvariant() with
            | "bundle"   -> Result.success (Some Shape.Bundle)
            | "ssdt"     -> Result.success (Some Shape.Ssdt)
            | "skeleton" -> Result.success (Some Shape.Skeleton)
            | "manifest" -> Result.success (Some Shape.Manifest)
            | other ->
                Result.failureOf (err "cli.config.flowShapeUnknown" (sprintf "flow '%s' shape '%s' is not bundle | ssdt | skeleton | manifest." name other))

    /// The opt-in per-flow `shaping` override (S6.4): a nested `"shaping"` object
    /// parsed leniently (`Config.parseLenient` over its raw JSON text) so the
    /// flow can narrow the global shaping for its own emission. Absent = `Ok None`
    /// (use the global shaping — byte-identical). A malformed sub-object surfaces
    /// its `Config` errors (D9 credential / type mismatch), named, never silent.
    let private parseFlowShaping (el: JsonElement) : Result<Config.Config option> =
        match el.TryGetProperty "shaping" with
        | true, s when s.ValueKind = JsonValueKind.Object ->
            Config.parseLenient (s.GetRawText()) |> Result.map Some
        | _ -> Result.success None

    /// A boolean flag property → its value (absent / non-bool ⇒ false).
    let private getBool (el: JsonElement) (name: string) : bool =
        match el.TryGetProperty name with
        | true, v when v.ValueKind = JsonValueKind.True -> true
        | _ -> false

    /// The data-load strategy this move declares (AUDIT, config-primary): an
    /// optional per-flow `"strategy": "merge" | "replace" | "fresh"`. Absent =
    /// `None` (the `Merge` default; `--fresh` forces `Fresh` per run). Closed; an
    /// unknown value is a NAMED refusal (mirrors `parseFlowScope`).
    let private parseFlowStrategy (name: string) (el: JsonElement) : Result<Strategy option> =
        match getString el "strategy" with
        | None -> Result.success None
        | Some raw ->
            match raw.ToLowerInvariant() with
            | "merge"   -> Result.success (Some Strategy.Merge)
            | "replace" -> Result.success (Some Strategy.Replace)
            | "fresh"   -> Result.success (Some Strategy.Fresh)
            | other ->
                Result.failureOf (err "cli.config.flowStrategyUnknown" (sprintf "flow '%s' strategy '%s' is not merge | replace | fresh." name other))

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
                // J2 — per-flow reconcile rules. Each entry must carry the
                // "<table>:<match-column>" shape (the same contract the
                // `--reconcile` tail proves via `TransferSpec.parseReconcileSpec`);
                // a malformed entry is a named config error, never a deferred
                // runtime surprise.
                let reconcileR : Result<string list> =
                    match el.TryGetProperty "reconcile" with
                    | true, r when r.ValueKind = JsonValueKind.Array ->
                        let entries =
                            [ for v in r.EnumerateArray() do
                                if v.ValueKind = JsonValueKind.String then
                                    match Option.ofObj (v.GetString()) with
                                    | Some s when not (String.IsNullOrWhiteSpace s) -> yield s.Trim()
                                    | _ -> () ]
                        let shapeErrors =
                            entries
                            |> List.collect (fun e ->
                                match TransferSpec.parseReconcileSpec e with
                                | Ok _ -> []
                                | Error _ -> [ err "cli.config.flowReconcileShape" (sprintf "flow '%s' reconcile entry '%s' is not <table>:<match-column>." name e) ])
                        if List.isEmpty shapeErrors then Result.success entries else Result.failure shapeErrors
                    | true, r when r.ValueKind <> JsonValueKind.Null ->
                        Result.failureOf (err "cli.config.flowReconcileShape" (sprintf "flow '%s' 'reconcile' must be an array of \"<table>:<match-column>\" strings." name))
                    | _ -> Result.success []
                match parseFlowScope name el, parseFlowShape name el, parseFlowShaping el, reconcileR, parseFlowStrategy name el with
                | Error es, _, _, _, _ | _, Error es, _, _, _ | _, _, Error es, _, _ | _, _, _, Error es, _ | _, _, _, _, Error es -> Result.failure es
                | Ok scope, Ok shape, Ok shaping, Ok reconcile, Ok strategy ->
                    Result.success
                        { Name = name; From = parseFlowSource el; To = toEnv; Rekey = getString el "rekey"
                          Tables = tables; Reconcile = reconcile; Scope = scope; Shape = shape; Shaping = shaping
                          // AUDIT (config-primary) — the flow's declared execution profile.
                          Strategy = strategy
                          Resumable = getBool el "resumable"
                          Streaming = getBool el "streaming"
                          Journal = getString el "journal" }

    /// Parse the `projection.json` document text into a `ProjectionConfig`.
    /// Aggregates every per-environment / per-flow error so the operator
    /// sees them all at once.
    /// NM-10 (2026-06-13) — the closed secondary-verb set, hoisted here so it
    /// is the SINGLE source for both the argv router (`Command.secondaryVerbs`
    /// = this) and the config-load collision check below. A flow named after
    /// one of these verbs is permanently unreachable: `Command.parse` matches
    /// the verb arms before the `cfg.Flows` map, so `projection report` runs
    /// the verb, never a `flows: { "report": … }` entry. `ProjectionConfig.parse`
    /// rejects such a flow at load (`cli.config.flowNameReservedVerb`) rather
    /// than let it parse, render, and list while reaching nothing (A44:
    /// expressible ⇔ reachable). THE_CLI.md §3.
    let reservedFlowVerbs : Set<string> =
        set [ "check"; "explain"; "seal"; "report"; "profile"; "synth-correct"; "init"; "diff"; "compare"
              "revert"; "slice-extract"; "slice-apply"; "slice-reset"; "slice-run" ]

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
                // NM-10 — a flow named after a reserved secondary verb is
                // shadowed by `Command.parse` (the verb arms precede the
                // `cfg.Flows` map) and is permanently unreachable. Refuse it at
                // load, naming the offending flow + that the name is a reserved
                // verb, sourced from `reservedFlowVerbs` (the same set the router
                // uses — no drifting copy).
                let reservedVerbCollisions =
                    flowResults
                    |> List.choose (function Ok f -> Some f.Name | Error _ -> None)
                    |> List.filter reservedFlowVerbs.Contains
                    |> List.map (fun name ->
                        err
                            "cli.config.flowNameReservedVerb"
                            (sprintf "flow '%s' is a reserved verb name; `projection %s` always runs the built-in verb, so the flow would be unreachable. Rename the flow." name name))
                let errors =
                    (envResults |> List.collect (function Error e -> e | Ok _ -> []))
                    @ (flowResults |> List.collect (function Error e -> e | Ok _ -> []))
                    @ reservedVerbCollisions
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
                    // The legacy top-level movement forms.
                    let legacyModel = getString root "model"
                    let legacyModelOssys = getString root "modelOssys"
                    // The shaping view of the SAME document, parsed leniently
                    // (a movement-only file defaults every shaping section).
                    // Any shaping error (D9 credential, type mismatch) surfaces
                    // here so the unified document is validated as one.
                    match Config.parseLenient json with
                    | Error es -> Result.failure es
                    | Ok shaping ->
                    // S2 — reconcile `model`/`modelOssys` into the one `model`
                    // namespace (THE_CONFIG_CONTROL_PLANE §4 collision table).
                    // The unified `model` OBJECT (path/ossys/modules) is the
                    // canonical form; the legacy top-level `model: "<path>"` maps
                    // onto `Shaping.Model.Path` and top-level `modelOssys` onto
                    // `Shaping.Model.Ossys`. The object form wins where it set a
                    // value (a present object Path/Ossys is canonical); the
                    // legacy form fills only what the object left absent — so the
                    // two forms agree on one `Shaping.Model`.
                    let reconciledModel : Config.ModelSection =
                        { shaping.Model with
                            Path  = (match shaping.Model.Path  with Some _ as p -> p | None -> legacyModel)
                            Ossys = (match shaping.Model.Ossys with Some _ as o -> o | None -> legacyModelOssys) }
                    // CROSS_ENVIRONMENT_READINESS §4 (model-from-environment) —
                    // `model.env` points the schema source into the `environments`
                    // registry BY NAME (like `flow.from` / `readiness.schema`),
                    // instead of inlining a connection. It is a movement-surface
                    // concept — it resolves against the registry the pure `Config`
                    // does not carry — so it is read here from the canonical
                    // `model` object (Config ignores the unread key) and resolved
                    // into `Shaping.Model.Ossys`, the live OSSYS source downstream
                    // consumers already read. The resolution is TRANSPARENT:
                    // `env: "cloud-dev"` yields exactly the `ossys` that env's
                    // `conn` declares, so nothing downstream changes. Refusals are
                    // named — `env` + a live `ossys` (two ways to name one source),
                    // an unknown environment, or a non-direct (bundle/docker) one.
                    let modelEnv =
                        match root.TryGetProperty "model" with
                        | true, m when m.ValueKind = JsonValueKind.Object -> getString m "env"
                        | _ -> None
                    let resolvedModelR : Result<Config.ModelSection> =
                        match modelEnv with
                        | None -> Result.success reconciledModel
                        | Some envName ->
                            match reconciledModel.Ossys with
                            | Some _ ->
                                Result.failureOf (err "cli.config.modelEnvAndOssys" (sprintf "model sets both 'env' (\"%s\") and a live 'ossys'/'modelOssys' source — two ways to name the one live schema source. Use 'env' to point into the environments registry, or 'ossys' for a standalone connection, not both." envName))
                            | None ->
                                match Map.tryFind envName environments with
                                | None ->
                                    let known = environments |> Map.toList |> List.map fst |> String.concat ", "
                                    let suffix = if known = "" then "no environments are configured." else sprintf "known environments: %s." known
                                    Result.failureOf (err "cli.config.modelEnvUnknown" (sprintf "model.env names environment '%s', which is not declared in 'environments'; %s" envName suffix))
                                | Some env ->
                                    match env.Access with
                                    | Access.Direct r -> Result.success { reconciledModel with Ossys = Some (connRefToSpec r) }
                                    | _ -> Result.failureOf (err "cli.config.modelEnvNotDirect" (sprintf "model.env names environment '%s', but it is not access:direct — a bundle/docker place has no live OSSYS connection to read the model from. Point model.env at a direct environment." envName))
                    match resolvedModelR with
                    | Error es -> Result.failure es
                    | Ok resolvedModel ->
                    let shaping = { shaping with Model = resolvedModel }
                    Result.success
                        { Environments = environments; Flows = flows
                          Model = legacyModel
                          ModelOssys = legacyModelOssys
                          Defaults = defaults
                          Shaping = shaping
                          // §11 — the synthetic-load policy baseline (a movement
                          // -vocabulary citizen; rendered, so it round-trips).
                          Synthetic = parseSynthetic root
                          // F7 — the blessed tightening relaxations (movement-
                          // vocabulary citizen; rendered, so it round-trips).
                          TighteningRelaxations = getStringArray root "tighteningRelaxations"
                          // Data-portability — the named use-case slice definitions
                          // (movement-vocabulary citizen; rendered, so it round-trips).
                          Slices = parseSlices root
                          // Data-portability — the named extract→apply slice flows.
                          SliceFlows = parseSliceFlows root
                          // CROSS_ENVIRONMENT_READINESS §4 — the readiness block;
                          // an omitted `schema` defaults to `model.env`.
                          Readiness = parseReadiness modelEnv root
                          // `parse` has no file provenance; `fromFile` overlays it.
                          SourcePath = None }
        with ex ->
            Result.failureOf (err "cli.config.parseFailed" (sprintf "projection.json did not parse: %s" ex.Message))

    /// Read and parse `projection.json` from disk; an absent file is the
    /// empty config (configuration is opt-in, not required).
    let fromFile (path: string) : Result<ProjectionConfig> =
        if not (File.Exists path) then Result.success empty
        else
            try
                // S6.2 — overlay the load provenance so resolveFlowSpec can route
                // the provenance-bearing publish arms (ConfigFile) for store sinks.
                parse (File.ReadAllText path)
                |> Result.map (fun cfg -> { cfg with SourcePath = Some path })
            with ex -> Result.failureOf (err "cli.config.readFailed" (sprintf "could not read '%s': %s" path ex.Message))

    // --- the inverse Ψ: render (the declarative dual of parse, G4) ----------
    // THE_CONFIG_CONTROL_PLANE §2/§3 (A44, clause 1 — faithfulness) + G4. The
    // declarative DUAL of `parseEnvironment` / `parseFlow`: a typed-DOM render
    // (`JsonObject`, not hand-authored strings — the declarative-test-inputs
    // discipline) over the SAME field vocabulary, so `parse ∘ render = id` on
    // the movement config DOM. Each renderer's field set is the exact mirror of
    // its parser's read set: env = `access`/`out`/`conn`/`grant`/`store`/
    // `rendition`; flow = `to`/`from`/`profile`/`scope`/`tables`/`rekey`.

    let private setStr (o: JsonObject) (name: string) (value: string) : unit =
        o.[name] <- JsonValue.Create value

    let private setOptStr (o: JsonObject) (name: string) (value: string option) : unit =
        match value with Some v -> setStr o name v | None -> ()

    /// The `conn` reference field text (the dual of `parseConnectionSpec`):
    /// `env:<VAR>` / `file:<path>` — never an inline secret (D9 holds by shape).
    /// Delegates to `connRefToSpec` (the same total function, hoisted above
    /// `parse` for `model.env` resolution).
    let private renderConnRef (r: ConnectionRef) : string = connRefToSpec r

    /// The grant refusal-gate field (dual of `parseGrant`): the canonical token.
    let private renderGrant (g: Grant) : string =
        match g with
        | Grant.SchemaAndData -> "schema+data"
        | Grant.DataOnly      -> "data"

    /// The rendition metadata field (dual of `parseRendition`).
    let private renderRendition (r: Rendition) : string =
        match r with
        | Rendition.Physical -> "physical"
        | Rendition.Logical  -> "logical"

    /// The archetype disposition field (dual of `parseArchetype`): the canonical
    /// token. Emitted only when DECLARED (`Some`) — an undeclared archetype
    /// round-trips through the absent arm (so existing configs render
    /// byte-identically; mirrors how `grant`/`rendition` omit their absent value).
    let private renderArchetype (a: Archetype) : string =
        match a with
        | Archetype.FullRights -> "full-rights"
        | Archetype.ManagedDml -> "managed-dml"

    /// The move-projection field (dual of `parseFlowScope`): the canonical token.
    let private renderScope (s: Scope) : string =
        match s with
        | Scope.Schema -> "schema"
        | Scope.Data   -> "data"
        | Scope.All    -> "both"

    /// The bundle-composition field (dual of `parseFlowShape`): the canonical
    /// token. Emitted only when `Some` — the `Bundle` default round-trips through
    /// the absent arm (mirrors how `scope` omits its default).
    let private renderShape (s: Shape) : string =
        match s with
        | Shape.Bundle   -> "bundle"
        | Shape.Ssdt     -> "ssdt"
        | Shape.Skeleton -> "skeleton"
        | Shape.Manifest -> "manifest"

    /// The data-load strategy field (dual of `parseFlowStrategy`): the canonical
    /// token. Emitted only when `Some` — `Merge` (the default) round-trips through
    /// the absent arm (mirrors `scope`/`shape`).
    let private renderStrategy (s: Strategy) : string =
        match s with
        | Strategy.Merge   -> "merge"
        | Strategy.Replace -> "replace"
        | Strategy.Fresh   -> "fresh"

    /// Render one `Environment` to its `JsonObject` — the dual of
    /// `parseEnvironment`. `access` + its companion (`out` for bundle, `conn`
    /// for direct; docker is bare) reconstruct the reach; `grant`/`store`/
    /// `rendition` carry the optional facets only when present.
    let renderEnvironment (env: Environment) : JsonObject =
        let o = JsonObject()
        (match env.Access with
         | Access.Bundle (out, conn) ->
             setStr o "access" "bundle"
             setStr o "out" out
             (match conn with Some r -> setStr o "conn" (renderConnRef r) | None -> ())
         | Access.Direct r   -> setStr o "access" "direct"; setStr o "conn" (renderConnRef r)
         | Access.Docker     -> setStr o "access" "docker")
        setOptStr o "grant" (env.Grant |> Option.map renderGrant)
        setOptStr o "store" env.Store
        setOptStr o "rendition" (env.Rendition |> Option.map renderRendition)
        setOptStr o "archetype" (env.Archetype |> Option.map renderArchetype)
        // M22/M23 — the dual of `parseEnvironment`'s atomicDeploy/revert (A44):
        // emitted only when present, so the derived-default arms round-trip absent.
        (match env.AtomicDeploy with
         | Some b -> o.["atomicDeploy"] <- System.Text.Json.Nodes.JsonValue.Create(b)
         | None   -> ())
        setOptStr o "revert" (env.Revert |> Option.map RevertPolicy.token)
        o

    /// Render one `Flow` to its `JsonObject` — the dual of `parseFlow`. `from`
    /// reconstructs the content origin (`model` is the absent default, so it is
    /// omitted; `synthetic` carries its `profile`; an env name is verbatim);
    /// `to`/`scope`/`tables`/`rekey` mirror the remaining fields.
    let renderFlow (flow: Flow) : JsonObject =
        let o = JsonObject()
        setStr o "to" flow.To
        (match flow.From with
         // `model` is the absent-`from` default (`parseFlowSource None`), so a
         // model flow omits `from` — round-trips through the default arm.
         | FlowSource.Model              -> ()
         | FlowSource.NoData             -> setStr o "from" "none"
         | FlowSource.Synthetic (profile, correction) ->
             setStr o "from" "synthetic"
             setOptStr o "profile" profile
             setOptStr o "correction" correction
         | FlowSource.Env e              -> setStr o "from" e)
        setOptStr o "scope" (flow.Scope |> Option.map renderScope)
        setOptStr o "shape" (flow.Shape |> Option.map renderShape)
        setOptStr o "rekey" flow.Rekey
        (if not (List.isEmpty flow.Tables) then
            let a = JsonArray()
            for t in flow.Tables do a.Add(JsonValue.Create t)
            o.["tables"] <- a)
        // J2 — the per-flow reconcile rules (the empty default round-trips
        // through the absent arm, mirroring `tables`).
        (if not (List.isEmpty flow.Reconcile) then
            let a = JsonArray()
            for r in flow.Reconcile do a.Add(JsonValue.Create r)
            o.["reconcile"] <- a)
        // AUDIT (config-primary) — the execution profile. Each omits its default
        // (Merge / false / None) so an existing flow round-trips byte-identically.
        setOptStr o "strategy" (flow.Strategy |> Option.map renderStrategy)
        (if flow.Resumable then o.["resumable"] <- JsonValue.Create true)
        (if flow.Streaming then o.["streaming"] <- JsonValue.Create true)
        setOptStr o "journal" flow.Journal
        o

    /// Render a whole movement config to its `projection.json` DOM — the inverse
    /// Ψ at the document level (G4). Only the movement vocabulary
    /// (`environments`/`flows`/`model`/`modelOssys`/`defaults`) is rendered; the
    /// shaping namespaces are out of this slice's round-trip (the movement-only
    /// document a flow authors). `parse ∘ render = id` on that DOM.
    let renderConfig (cfg: ProjectionConfig) : JsonObject =
        let root = JsonObject()
        (if not (Map.isEmpty cfg.Environments) then
            let envs = JsonObject()
            for KeyValue (name, env) in cfg.Environments do envs.[name] <- renderEnvironment env
            root.["environments"] <- envs)
        (if not (Map.isEmpty cfg.Flows) then
            let flows = JsonObject()
            for KeyValue (name, flow) in cfg.Flows do flows.[name] <- renderFlow flow
            root.["flows"] <- flows)
        setOptStr root "model" cfg.Model
        setOptStr root "modelOssys" cfg.ModelOssys
        (if not (Map.isEmpty cfg.Defaults) then
            let d = JsonObject()
            for KeyValue (k, v) in cfg.Defaults do setStr d k v
            root.["defaults"] <- d)
        // F7 (audit 2026-06-17) — the blessed tightening relaxations. Omitted
        // when empty, so a config with no blessings round-trips to no key
        // (`parse ∘ render = id`); emitted as the `kind.column` string array the
        // `RelaxationStore` surgical merge also targets (same key, no conflict).
        (if not (List.isEmpty cfg.TighteningRelaxations) then
            let a = JsonArray()
            for k in cfg.TighteningRelaxations do a.Add(JsonValue.Create k)
            root.["tighteningRelaxations"] <- a)
        // §11 — the synthetic-load policy block. Omitted when it is the default
        // (every knob absent), so a config with no `synthetic` round-trips to no
        // `synthetic` key (`parse ∘ render = id`).
        (if cfg.Synthetic <> Config.defaultSyntheticSection then
            let s = JsonObject()
            (match cfg.Synthetic.PreserveCardinalityMax with Some n -> s.["preserveCardinalityMax"] <- JsonValue.Create n | None -> ())
            (if not (List.isEmpty cfg.Synthetic.Preserve) then
                let a = JsonArray()
                for v in cfg.Synthetic.Preserve do a.Add(JsonValue.Create v)
                s.["preserve"] <- a)
            (if not (List.isEmpty cfg.Synthetic.Synthesize) then
                let a = JsonArray()
                for v in cfg.Synthetic.Synthesize do a.Add(JsonValue.Create v)
                s.["synthesize"] <- a)
            (match cfg.Synthetic.Scale with Some d -> s.["scale"] <- JsonValue.Create d | None -> ())
            (match cfg.Synthetic.Seed with Some n -> s.["seed"] <- JsonValue.Create n | None -> ())
            (if cfg.Synthetic.WeightVolumeByCentrality then s.["weightVolumeByCentrality"] <- JsonValue.Create true)
            (if cfg.Synthetic.ClusterFksByContext then s.["clusterFksByContext"] <- JsonValue.Create true)
            root.["synthetic"] <- s)
        // Data-portability — the named use-case slice definitions. Each spec is
        // rendered via `SliceCodec` (the same serializer the round-trip law is
        // proven over) and re-parsed into a node. Omitted when empty, so a
        // config with no slices round-trips to no `slices` key (A44).
        (if not (Map.isEmpty cfg.Slices) then
            let s = JsonObject()
            for KeyValue (name, spec) in cfg.Slices do
                s.[name] <- System.Text.Json.Nodes.JsonNode.Parse(Projection.Targets.Json.SliceCodec.serialize spec)
            root.["slices"] <- s)
        // Data-portability — the named extract→apply slice flows. Omitted when
        // empty, so a config with no slice flows round-trips to no key (A44).
        (if not (Map.isEmpty cfg.SliceFlows) then
            let f = JsonObject()
            for KeyValue (name, sf) in cfg.SliceFlows do
                let o = JsonObject()
                setStr o "source" sf.Source
                setStr o "slice"  sf.Slice
                setStr o "target" sf.Target
                f.[name] <- o
            root.["sliceFlows"] <- f)
        // CROSS_ENVIRONMENT_READINESS §4 — the readiness block. Omitted when
        // absent, so a config with no `readiness` round-trips to no key (A44).
        (match cfg.Readiness with
         | Some rs ->
             let o = JsonObject()
             setStr o "schema" rs.Schema
             (if not (List.isEmpty rs.Confirm) then
                 let a = JsonArray()
                 for c in rs.Confirm do a.Add(JsonValue.Create c)
                 o.["confirm"] <- a)
             root.["readiness"] <- o
         | None -> ())
        root

    /// Render a movement config to its `projection.json` text — the round-trip
    /// witness `parse (render cfg) = Ok cfg` (A44 clause 1).
    let render (cfg: ProjectionConfig) : string =
        (renderConfig cfg).ToJsonString()

[<RequireQualifiedAccess>]
module Command =

    let private err (code: string) (message: string) : ValidationError =
        ValidationError.create code message

    /// Reconstruct the out-of-band connection spec from a resolved ref.
    let connSpecOf (r: ConnectionRef) : string =
        match r with
        | ConnectionRef.EnvVar n -> "env:" + n
        | ConnectionRef.File p   -> "file:" + p
        | ConnectionRef.Raw c    -> "live:" + c

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
                | Access.Bundle (_, Some r) -> Result.success (connSpecOf r)
                | _ -> Result.failureOf (err "cli.env.notLive" (sprintf "environment '%s' has no live connection (set access:direct, or add a `conn` to the bundle environment)." raw))
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
          Streaming   = spec.Streaming
          Journal     = spec.Journal
          Atomic      = spec.Atomic
          RevertPolicy = spec.RevertPolicy
          RevertDir   = spec.RevertDir
          Store       = spec.Store
          Env         = spec.Env
          Tables      = spec.Tables
          Seed        = spec.Seed
          Scale       = spec.Scale
          Correction  = spec.Correction
          SinkCapability = spec.SinkCapability }

    // --- the pure movement routing (the surface→engine map) ----------------

    /// Route a resolved `MovementSpec` to its engine face — purely. A flow
    /// resolves to one of these specs; the totality test sweeps the space.
    let planMovement (cfg: ProjectionConfig) (spec: MovementSpec) : ExecutionPlan =
        let opts = optsOf spec
        let modelMissing prefix = PlanAction.Refused (1, err "cli.move.modelMissing" (prefix + noModel))
        let dataConn (alias: string) : Result<string> = resolveLiveConn cfg alias
        // Live OSSYS (primary) when configured; the model file (fallback) is
        // optional then. `hasModel` is true when either source is available.
        // S2 — read the OSSYS source from the canonical `Shaping.Model.Ossys`
        // (the legacy top-level `modelOssys` is reconciled into it by the
        // loader), so the object `model.ossys` and legacy forms thread identically.
        let modelOssys = cfg.Shaping.Model.Ossys
        let hasModel = spec.Model <> ModelSource.Unspecified || Option.isSome modelOssys
        let action =
            match spec.Destination with
            | Destination.Folder dir ->
                match spec.Model with
                | ModelSource.ConfigFile c -> PlanAction.PublishBundle (c, dir, spec.Store, spec.Env)
                | _ when hasModel ->
                    match spec.Shape with
                    | Shape.Skeleton            -> PlanAction.EmitSkeleton (spec.Model, modelOssys, dir)
                    | Shape.Manifest            -> PlanAction.EmitManifest (spec.Model, modelOssys, dir)
                    | Shape.Bundle | Shape.Ssdt -> PlanAction.EmitBundle (spec.Model, modelOssys, dir)
                | _ -> modelMissing "projection: "
            | Destination.Docker ->
                if hasModel then PlanAction.DeployDocker (spec.Model, modelOssys)
                else modelMissing "projection (docker): "
            | Destination.Live connRef ->
                let conn = connSpecOf connRef
                let schemaOnly = (spec.Scope = Scope.Schema)
                let dataOnly = (spec.Scope = Scope.Data)
                // G2 — the DERIVED direction routes a B→A legacy data move to the
                // reverse-leg runner; a same-rendition peer (A→A) / down-leg keeps
                // the generic `Transfer`. The engine cannot make this distinction
                // by `DataOrigin` alone (both are `FromTarget`).
                let dataMove (src: string) (execute: bool) : PlanAction =
                    match spec.Direction with
                    | MovementDirection.UpLegacy ->
                        // J3 — the reverse leg's two SsKey-aligned contracts
                        // are RENDERED from the ONE authored model
                        // (`CatalogRendition`); with no model there is nothing
                        // to render, so the refusal is at PLAN time, named.
                        if hasModel then PlanAction.RunReverseLeg (spec.Model, modelOssys, src, conn, opts, execute)
                        else modelMissing "projection (reverse leg): the B→A reverse leg renders its contracts from the authored model. "
                    | MovementDirection.UpPeer ->
                        // The peer (A→A) move — two deployed cells of one model
                        // whose physical `OSUSR_*` names differ per espace. The
                        // peer runner reads a contract from EACH side's OSSYS
                        // metamodel (native GUID identity), so no authored model
                        // rides the action; the shape gate + subset-FK gate own
                        // the pre-write refusals. 2026-07-06. (An env→env flow
                        // with UNSET renditions keeps the name-blind `Transfer`
                        // below — the identical-rendition escape hatch.)
                        PlanAction.TransferPeer (src, conn, opts, execute)
                    | _ -> PlanAction.Transfer (src, conn, opts, execute)
                if not spec.Commit then
                    match spec.Data with
                    | DataOrigin.FromTarget alias when not schemaOnly ->
                        match dataConn alias with
                        | Ok src -> dataMove src false
                        | Error es -> PlanAction.Refused (6, List.head es)
                    | DataOrigin.Synthetic profile when not schemaOnly ->
                        PlanAction.SynthesizeAndLoad (spec.Model, modelOssys, profile, conn, opts, false, cfg.Shaping.Model, cfg.Synthetic)
                    | _ ->
                        if hasModel then PlanAction.PreviewSchema (spec.Model, modelOssys, conn, opts.Declaration)
                        else modelMissing "projection: "
                else
                    match spec.Data with
                    | DataOrigin.FromTarget alias when not schemaOnly ->
                        match dataConn alias with
                        | Error es -> PlanAction.Refused (6, List.head es)
                        | Ok src ->
                            if dataOnly then dataMove src true
                            elif hasModel then PlanAction.MigrateWithData (spec.Model, modelOssys, conn, src, opts)
                            else modelMissing "projection: "
                    | DataOrigin.Synthetic profile when not schemaOnly ->
                        if dataOnly then PlanAction.SynthesizeAndLoad (spec.Model, modelOssys, profile, conn, opts, true, cfg.Shaping.Model, cfg.Synthetic)
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
            | _, PlanAction.TransferPeer _ -> []
            | _, PlanAction.RunReverseLeg _ -> []
            | _, PlanAction.SynthesizeAndLoad _ -> []
            | _ -> [ "--fresh accepted; this action has no selectable data-load strategy (incremental applied)." ]
        // The resumable/idempotent envelope (G10) is honored on the pure-transfer
        // data leg only; any other action carries no resumable write seam, so the
        // flag is noted, never silently dropped (THE_VOICE no-silent-drop).
        let resumableNote =
            match spec.Resumable, action with
            | false, _ -> []
            | true, PlanAction.Transfer _ -> []
            | true, PlanAction.TransferPeer _ -> []
            | true, PlanAction.RunReverseLeg _ -> []
            | true, _ -> [ "--resumable accepted; this action has no resumable data-load seam (standard write applied)." ]
        // D8 — seed/scale are honored on the synthetic load only; on any other
        // action they are noted, never silently dropped (THE_VOICE no-silent-drop).
        let synthesisNote =
            match (spec.Seed, spec.Scale), action with
            | (None, None), _ -> []
            | _, PlanAction.SynthesizeAndLoad _ -> []
            | _, _ -> [ "--seed/--scale accepted; this action has no synthesis leg (model data applied)." ]
        // NM-07 — the streaming realization + its capture journal are honored on
        // the reverse leg only (the sole `opts.Streaming`/`opts.Journal` consumer,
        // `runReverseLegTransfer`); any other action carries no streaming/journaled
        // write seam, so the flags are noted, never silently dropped.
        let streamingNote =
            match spec.Streaming, action with
            | false, _ -> []
            | true, PlanAction.RunReverseLeg _ -> []
            // The peer leg rides the reverse-leg realization selector, so an
            // explicit --streaming is honored (or refused BY NAME on an
            // inadmissible combination — never silently dropped).
            | true, PlanAction.TransferPeer _ -> []
            | true, _ -> [ "--streaming accepted; this action has no streaming write seam (materialized write applied)." ]
        let journalNote =
            match spec.Journal, action with
            | None, _ -> []
            | Some _, PlanAction.RunReverseLeg _ -> []
            | Some _, PlanAction.TransferPeer _ -> []
            | Some _, _ -> [ "--journal accepted; this action has no journaled write seam (unjournaled write applied)." ]
        // F0c-I/O — the blessed correction is consumed only by the synthetic load
        // (it threads `Profile ⊕ Correction` into σ + drives Faker realization);
        // on any other action it is noted, never silently dropped.
        let correctionNote =
            match spec.Correction, action with
            | None, _ -> []
            | Some _, PlanAction.SynthesizeAndLoad _ -> []
            | Some _, _ -> [ "correction accepted; this action has no synthesis leg (no correction applied)." ]
        // The name-blind fallback is a silent assumption worth NAMING (the
        // no-silent-downgrade discipline): an env→env DATA move whose
        // renditions are unset rides the generic `Transfer`, which reads ONE
        // contract from the source and writes with the SOURCE's physical
        // names — correct only when the sink's names match. The peer leg
        // (`rendition: physical` on both sides) carries the SsKey-aligned
        // shape + subset-FK gates. 2026-07-06 (ergonomics pass, proposal 2).
        let renditionNote =
            match spec.Direction, spec.Data, action with
            | MovementDirection.Down, DataOrigin.FromTarget _, PlanAction.Transfer _ ->
                [ "this flow moves data between two live environments with `rendition` unset — the transfer assumes MATCHING physical table names on both sides. Set `rendition: physical` on both environments to run the SsKey-aligned peer leg (shape + subset-FK gates) instead." ]
            | _ -> []
        { Notes = unhonoredNotes spec @ freshNote @ resumableNote @ synthesisNote @ streamingNote @ journalNote @ correctionNote @ renditionNote; Action = action }


    let planCompare (_cfg: ProjectionConfig) (args: string list) : ExecutionPlan =
        let valueOf = flagValue args
        let action =
            match args with
            | a :: b :: _ -> PlanAction.Compare (a, b, (valueOf "--format" = Some "json"))
            | _ ->
                PlanAction.Refused (
                    2,
                    err "cli.compare.args" "projection compare: needs two references — projection compare <A> <B>.")
        { Notes = []; Action = action }

    /// `revert [--script <path>] --against <env> [--go]` — the deliberate
    /// undo (2026-07-06): run the DELETE-by-captured-key artifact a
    /// successful transfer wrote (`transfer-undo.sql`) — or a failed run's
    /// `transfer-revert.sql` — against the named environment. Preview is
    /// the default; `--go` (+ the env gate) executes.
    let planRevert (cfg: ProjectionConfig) (args: string list) : ExecutionPlan =
        let valueOf = flagValue args
        let script = valueOf "--script" |> Option.defaultValue "transfer-undo.sql"
        let go = List.contains "--go" args
        let force = List.contains "--force" args
        let action =
            match valueOf "--against" with
            | None ->
                PlanAction.Refused (2, err "cli.revert.args" "projection revert: needs --against <environment> (the sink the undo runs against). Optional: --script <path> (default ./transfer-undo.sql), --go.")
            | Some envLabel ->
                match resolveLiveConn cfg envLabel with
                | Error es -> PlanAction.Refused (6, List.head es)
                | Ok conn -> PlanAction.RevertScript (script, envLabel, conn, go, force)
        { Notes = []; Action = action }

    let planExplain (cfg: ProjectionConfig) (args: string list) : ExecutionPlan =
        let valueOf = flagValue args
        let depthOpt =
            match valueOf "--depth" with
            | Some "all" -> Some System.Int32.MaxValue
            | Some d -> (match System.Int32.TryParse d with | true, n -> Some (max 0 n) | _ -> None)
            | None -> None
        let action =
            match args with
            | "diff" :: a :: b :: _ -> PlanAction.ExplainDiff (a, b, (valueOf "--format" = Some "json"), depthOpt, valueOf "--only", valueOf "--module")
            | "policy" :: a :: b :: _ -> PlanAction.ExplainPolicy (a, b)
            | "node" :: c :: k :: _   -> PlanAction.ExplainNode (c, k, (valueOf "--format" = Some "json"), depthOpt)
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
        | Access.Bundle (out, _) -> Destination.Folder out
        | Access.Direct r        -> Destination.Live r
        | Access.Docker          -> Destination.Docker

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
                // A bundle place with a read `conn` is a live reverse-leg source:
                // schema was published DOWN as files; data is read UP live.
                | Access.Bundle (_, Some r) -> Some (env, connSpecOf r)
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
        | FlowSource.Synthetic (Some profile, _) -> Result.success (DataOrigin.Synthetic profile)
        | FlowSource.Synthetic (None, _) ->
            Result.failureOf (err "cli.flow.syntheticNoProfile" "flow source `synthetic` needs a `profile` (e.g. \"profile\": \"file:legacy.profile.json\") — the evidence the generator replays.")
        | FlowSource.Env e ->
            match Map.tryFind e cfg.Environments with
            | None ->
                Result.failureOf (err "cli.flow.fromUnknown" (sprintf "flow source environment '%s' is not defined." e))
            | Some env ->
                match env.Access with
                | Access.Direct r -> Result.success (DataOrigin.FromTarget (connSpecOf r))
                // A bundle place with a read `conn` is a live read source (the
                // reverse leg reads UP from the real on-prem database, even though
                // schema is published DOWN to it as a file bundle).
                | Access.Bundle (_, Some r) -> Result.success (DataOrigin.FromTarget (connSpecOf r))
                | _ -> Result.failureOf (err "cli.flow.fromNotDirect" (sprintf "flow source '%s' has no live connection to read rows from — use access:direct, or add a `conn` to the bundle environment (the reverse-leg read path)." e))

    /// G2 — DERIVE the movement direction (a binding, never parsed) from the
    /// flow's source/sink renditions + content origin (THE_CONFIG_CONTROL_PLANE
    /// §3). Synthetic mint → `UpSynthetic`; a recognized B→A reverse leg (logical
    /// source → physical sink — the same predicate `reverseLegOf` derives) →
    /// `UpLegacy`; a physical→physical env-to-env peer/golden move → `UpPeer`;
    /// everything else (model → bundle/live A→B down-leg, NoData) → `Down`. The
    /// router distinguishes `UpLegacy` (the reverse leg) from `UpPeer` (a peer
    /// transfer) — which it cannot do by `DataOrigin` alone.
    let private directionOf (cfg: ProjectionConfig) (flow: Flow) : MovementDirection =
        match flow.From with
        | FlowSource.Synthetic _ -> MovementDirection.UpSynthetic
        | FlowSource.Model | FlowSource.NoData -> MovementDirection.Down
        | FlowSource.Env sourceName ->
            // The reverse leg is exactly the `reverseLegOf` predicate (logical
            // source → physical live sink); reuse it so the two never drift.
            match reverseLegOf cfg flow with
            | Some _ -> MovementDirection.UpLegacy
            | None ->
                // A same-rendition env→env move: physical→physical is the A→A
                // peer/golden re-key. Any other env→env shape (e.g. a bundle
                // sink, or unset renditions) rides the established down/transfer
                // routing — `Down` (the router treats it identically today).
                let renditionOf envName =
                    Map.tryFind envName cfg.Environments |> Option.bind (fun e -> e.Rendition)
                match renditionOf sourceName, renditionOf flow.To with
                | Some Rendition.Physical, Some Rendition.Physical -> MovementDirection.UpPeer
                | _ -> MovementDirection.Down

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
                // S6.2 (G3, operator decision 1 — wire ConfigFile into flows): the
                // publish-with-provenance arms (`PublishBundle`/`PublishAndLoad`)
                // fire only on `ModelSource.ConfigFile`. Emit it exactly when the
                // flow targets a PROVENANCE-BEARING place — the config was loaded
                // from a file (`SourcePath = Some`), the sink carries a `store`
                // (provenance is configured), AND there is a model to publish.
                // WP9 (DECISIONS 2026-06-13; event-ledger #1): "a model to
                // publish" is `Shaping.Model.Path` OR `Shaping.Model.Ossys` — an
                // OSSYS-sourced config (no `model.path`) is a first-class publish
                // source, so it fires provenance too. Every store-less target and
                // every from-string config keeps the byte-identical
                // ModelFile/Unspecified path, so the empty/default-config
                // invariant holds (path-sourced and store-less configs are
                // unchanged; only ossys-only-with-store gains provenance).
                let hasPublishableModel =
                    Option.isSome cfg.Shaping.Model.Path || Option.isSome cfg.Shaping.Model.Ossys
                // Slice C — derive the sink's capability-derived engine inputs from
                // its DECLARED-or-inferred archetype (DATABASE_ARCHETYPES.md §4).
                // `FullRights` (IDENTITY_INSERT + CREATE TABLE) ⇒ PreferPreservedKeys
                // + sink-resident resume; `ManagedDml` / undeclared ⇒ `structural`
                // (byte-identical). The two engine bits are projected here, the one
                // site that sees both the sink `Environment` and `CapabilityProfile`.
                let sinkCapability : SinkLoadCapability =
                    match Environment.effectiveArchetype toEnv with
                    | Some archetype ->
                        let profile = CapabilityProfile.``of`` archetype
                        { IdentityPolicy =
                            (if profile.IdentityInsert then IdentityPolicy.PreferPreservedKeys else IdentityPolicy.Structural)
                          SinkResidentResume =
                            (match profile.ResumeCheckpoint with
                             | ResumeKind.SinkResidentTable -> true
                             | ResumeKind.ClientJournal     -> false) }
                    | None -> SinkLoadCapability.structural
                let modelSource =
                    match cfg.SourcePath, toEnv.Store with
                    | Some sourcePath, Some _ when hasPublishableModel -> ModelSource.ConfigFile sourcePath
                    | _ ->
                        match cfg.Shaping.Model.Path with
                        | Some m -> ModelSource.ModelFile m
                        | None   -> ModelSource.Unspecified
                // M22 — derive the atomic schema-deploy disposition: ON for a
                // direct full-access sink, OFF otherwise; the env `atomicDeploy`
                // config overrides the default; `--no-atomic` overrides per run.
                let atomicDefault =
                    match toEnv.Access, Environment.effectiveArchetype toEnv with
                    | Access.Direct _, Some Archetype.FullRights -> true
                    | _ -> false
                let atomicResolved = (toEnv.AtomicDeploy |> Option.defaultValue atomicDefault) && not opts.NoAtomic
                // M23 — resolve the revert policy: `--auto-revert` forces Auto,
                // else the sink's configured `revert`, else the Script default.
                let revertResolved =
                    if opts.AutoRevert then RevertPolicy.Auto
                    else toEnv.Revert |> Option.defaultValue RevertPolicy.def
                // AUDIT (config-primary) — the data-load strategy: `--fresh` forces
                // `Fresh` per run, else the flow's declared `strategy`, else `Merge`.
                let resolvedStrategy =
                    if opts.Fresh then Strategy.Fresh
                    else flow.Strategy |> Option.defaultValue Strategy.Merge
                Result.success
                    { baseSpec with
                        // S2/S6.2 — the model is read from the canonical
                        // `Shaping.Model` (the unified `model` namespace, with
                        // the legacy top-level `model:"<path>"` reconciled in by
                        // the loader). Routes through `ConfigFile` for a
                        // provenance-bearing sink (see `modelSource` above).
                        Model    = modelSource
                        // S4a (G1) — the move's PROJECTION, decoupled from the
                        // target's `grant`. The per-flow `scope` wins when set
                        // (the schema-only / data-only legs become reachable);
                        // absent, the grant-derived default holds (back-compat:
                        // a `data`-granting target ⇒ Scope.Data, else All). `grant`
                        // stays the refusal gate in `planFlow`; this only sets the
                        // projection the engine carries.
                        Scope    = (flow.Scope |> Option.defaultWith (fun () ->
                                        match toEnv.Grant with Some Grant.DataOnly -> Scope.Data | _ -> Scope.All))
                        // S6.1 (decision 2) — the bundle composition this move
                        // emits. `None` = the `Bundle` default (byte-identical:
                        // a folder model flow keeps emitting the full pass-chain
                        // bundle); `skeleton` makes `EmitSkeleton` flow-reachable.
                        Shape    = (flow.Shape |> Option.defaultValue Shape.Bundle)
                        // S4b (G2) — the DERIVED direction (a binding from
                        // renditions + origin, never parsed). `planMovement`
                        // routes `UpLegacy` to the reverse-leg runner.
                        Direction = directionOf cfg flow
                        // AUDIT — config strategy + `--fresh` override (above). A
                        // `Fresh` move forces A = ∅ (genesis); merge/replace keep
                        // the auto-derived baseline.
                        Strategy = resolvedStrategy
                        Data     = data
                        Baseline = (if resolvedStrategy = Strategy.Fresh then Baseline.Empty else Baseline.Auto)
                        Rekey    = flow.Rekey
                        // J2 — the flow's declarative MatchByColumn re-key rules
                        // (e.g. the golden flow's User-by-email reconcile) ride
                        // the spec into the transfer leg's `LoadOpts.Reconcile`.
                        Reconcile = flow.Reconcile
                        Tables   = flow.Tables
                        AllowDrops = opts.AllowDrops
                        AllowCdc = opts.AllowCdc
                        // AUDIT (config-primary) — the execution profile: the flow's
                        // declared baseline, with the CLI flag as the per-run override
                        // (`--resumable`/`--streaming` force ON; `--journal` overrides
                        // the dir). Absent config + absent flag = byte-identical.
                        Resumable = (flow.Resumable || opts.Resumable)
                        Streaming = (flow.Streaming || opts.Streaming)
                        Journal = (opts.Journal |> Option.orElse flow.Journal)
                        Atomic = atomicResolved
                        RevertPolicy = revertResolved
                        RevertDir = opts.RevertDir
                        // D8 — the synthesis knobs ride the per-run intent
                        // (seed/volume vary at the moment of action, never config).
                        Seed = opts.Seed
                        Scale = opts.Scale
                        // F0c-I/O — the blessed correction-artifact ref: the
                        // `--correction` per-run override WINS when set, else the
                        // flow's declared `correction` (a synthetic source's
                        // sibling of `profile`). A non-synthetic flow has no
                        // declared correction, so `--correction` alone still
                        // threads (noted as inert by `planMovement` if the action
                        // has no synthesis leg).
                        Correction =
                            (opts.Correction
                             |> Option.orElse
                                 (match flow.From with
                                  | FlowSource.Synthetic (_, c) -> c
                                  | _ -> None))
                        // The target's durable timeline: a live --go records an
                        // episode into it (which `report` later diffs). F4.
                        Store    = toEnv.Store
                        // Slice C — the sink's archetype-derived engine inputs.
                        SinkCapability = sinkCapability
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
                        | false, (PlanAction.Transfer _ | PlanAction.RunReverseLeg _) -> []
                        | false, _ -> [ sprintf "flow tables (%s) apply to the data-transfer leg only; this action moves the full model." (String.concat ", " flow.Tables) ]
                    { plan with Notes = plan.Notes @ tableNote }

    /// Route a `report` verb tail (THE_CLI.md §8): `report <flow>` reads the
    /// flow's target durable timeline (its `store`) and renders the recorded
    /// ChangeManifest series — what changed since the last sealed episode. An
    /// explicit `--store <path>` overrides; a target with no store is refused
    /// (named, never silent). The bundle itself is built by the runner.
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
            | "go" :: rest ->
                // THE GO BOARD — resolve the named flow through the SAME
                // planning path a real run takes (preview opts), so the board
                // judges exactly what `--go` would execute (A44: the check
                // and the run cannot drift).
                // Positionals skip flags AND a value-bearing flag's value
                // token (`--format json` previously counted `json` as a
                // second positional and refused; caught 2026-07-07).
                let positionals =
                    let rec walk (args: string list) =
                        match args with
                        | [] -> []
                        | a :: _ :: tl when a = "--format" -> walk tl
                        | a :: tl when a.StartsWith "--" -> walk tl
                        | a :: tl -> a :: walk tl
                    walk rest
                match positionals with
                | [ flowName ] ->
                    match Map.tryFind flowName cfg.Flows with
                    | None ->
                        PlanAction.Refused (2, err "cli.check.goUnknownFlow" (sprintf "projection check go: flow '%s' is not defined in projection.json." flowName))
                    | Some flow ->
                        let previewOpts : FlowRunOpts =
                            { Go = false; Fresh = false; AllowDrops = false; AllowCdc = false
                              Resumable = false; Streaming = false; Journal = None; NoAtomic = false
                              AutoRevert = false; RevertDir = None; Seed = None; Scale = None; Correction = None }
                        let fromLabel = match flow.From with FlowSource.Env e -> e | FlowSource.Model -> "model" | FlowSource.Synthetic _ -> "synthetic" | FlowSource.NoData -> "none"
                        let asJson =
                            match rest |> List.pairwise |> List.tryFind (fun (a, _) -> a = "--format") with
                            | Some (_, v) -> v = "json"
                            | None -> false
                        let emitSql = List.contains "--sql" rest
                        PlanAction.CheckGo (flowName, fromLabel, flow.To, asJson, emitSql, (planFlow cfg flow previewOpts).Action)
                | _ ->
                    PlanAction.Refused (2, err "cli.check.goArgs" "projection check go: requires exactly one flow name (projection check go <flow> [--sql] [--format json]).")
            | "shape" :: _ ->
                match cfg.Readiness with
                | None ->
                    PlanAction.Refused (2, err "cli.check.shapeNoBlock" "projection check shape: no `readiness` block in projection.json (set readiness.schema + readiness.confirm).")
                | Some rs ->
                    // Resolve each env name to (label, D9 conn-ref) for the OSSYS
                    // read; a non-direct or unknown env is a NAMED refusal.
                    let refOf (envName: string) : Result<string * string> =
                        match Map.tryFind envName cfg.Environments with
                        | Some env ->
                            match env.Access with
                            | Access.Direct r -> Result.success (envName, connSpecOf r)
                            | _ -> Result.failureOf (err "cli.check.shapeNotDirect" (sprintf "readiness environment '%s' is not access:direct (no live OSSYS connection to read)." envName))
                        | None -> Result.failureOf (err "cli.check.shapeUnknownEnv" (sprintf "readiness environment '%s' is not defined." envName))
                    let agreedR = refOf rs.Schema
                    let confirmRs = rs.Confirm |> List.map refOf
                    let errors =
                        (match agreedR with Error es -> es | Ok _ -> [])
                        @ (confirmRs |> List.collect (function Error es -> es | Ok _ -> []))
                    match errors with
                    | e :: _ -> PlanAction.Refused (6, e)
                    | [] ->
                        let agreed = match agreedR with Ok v -> v | Error _ -> (rs.Schema, "")
                        let confirm = confirmRs |> List.choose (function Ok v -> Some v | Error _ -> None)
                        PlanAction.CheckShape (fst agreed, snd agreed, confirm, (valueOf "--format" = Some "json"))
            | _ ->
                match args |> List.tryFind (fun a -> not (a.StartsWith "--") && a <> "fidelity") with
                | Some path -> PlanAction.CheckCanary (path, List.contains "--cdc-silence" args)
                | None -> PlanAction.Refused (1, err "cli.check.noDdl" "projection check: the fidelity canary needs a source DDL path (check <source.sql>).")
        { Notes = []; Action = action }

    /// Route an `explain` verb tail to its understanding action — purely.
    /// `explain <flow>` is the live preview: what publishing the flow would
    /// change against its target's last sealed episode (B = the flow's model,
    /// A_prior = the target store) — the preview sibling to `report`'s history.
    /// `compare <A> <B>` — NM-71/WP9: resolve two operand refs and run the
    /// read-only readiness compare (schema delta + data dealbreakers). The face
    /// writes `compare.json` + prints the roll-up. Two refs are required.
    let planReport (cfg: ProjectionConfig) (args: string list) : ExecutionPlan =
        // The flow target's bundle `out` folder, when one is configured — the
        // directory the full-export feeding this timeline wrote `fidelity.json`
        // into, threaded so the report verb surfaces the Model Fidelity Report
        // without guessing (the prior candidate list only knew dirname(store) /
        // `out/` / cwd, so a flow whose bundle dir was none of those lost it).
        let bundleOutOf (envName: string) : string option =
            Map.tryFind envName cfg.Environments
            |> Option.bind (fun e -> match e.Access with Access.Bundle (out, _) -> Some out | _ -> None)
        let storeOf () : Result<string * string option> =
            match flagValue args "--store" with
            | Some s -> Result.success (s, None)
            | None ->
                match args |> List.tryFind (fun a -> not (a.StartsWith "--")) with
                | None -> Result.failureOf (err "cli.report.noFlow" "projection report: name a flow (report <flow>) or pass --store <path>.")
                | Some flowName ->
                    match Map.tryFind flowName cfg.Flows with
                    | None -> Result.failureOf (err "cli.report.unknownFlow" (sprintf "report: unknown flow '%s'." flowName))
                    | Some flow ->
                        match Map.tryFind flow.To cfg.Environments |> Option.bind (fun e -> e.Store) with
                        | Some store -> Result.success (store, bundleOutOf flow.To)
                        | None -> Result.failureOf (err "cli.report.noStore" (sprintf "report '%s': target environment '%s' has no `store` (the durable timeline); add one or pass --store <path>." flowName flow.To))
        match storeOf () with
        | Ok (store, outDir) -> { Notes = []; Action = PlanAction.ReportBundle (store, outDir) }
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

    /// Route a `synth-correct` verb tail (FUZZING §2.2, slice F0c-I/O):
    /// `synth-correct --out <path>` proposes a first-draft blessed-correction
    /// artifact from the CONFIGURED model's catalog (the proposer types PII by
    /// attribute name, so it needs the catalog — the `Profile` keys by `SsKey`
    /// and carries no names). The model resolves the way every model-bearing
    /// action does: live OSSYS (`model.ossys`) primary, the model file fallback;
    /// a config with neither is a named refusal (there is nothing to propose
    /// from). The durable sibling of `profile` — both write a reviewable hinge.
    let planSynthCorrect (cfg: ProjectionConfig) (args: string list) : ExecutionPlan =
        let valueOf = flagValue args
        let modelOssys = cfg.Shaping.Model.Ossys
        let modelSource =
            match cfg.Shaping.Model.Path with
            | Some m -> ModelSource.ModelFile m
            | None   -> ModelSource.Unspecified
        let hasModel = modelSource <> ModelSource.Unspecified || Option.isSome modelOssys
        let action =
            match valueOf "--out" with
            | None ->
                PlanAction.Refused (2, err "cli.synthCorrect.noOut" "projection synth-correct: requires --out <path> (the correction artifact file to write).")
            | Some out ->
                if hasModel then PlanAction.ProposeCorrection (modelSource, modelOssys, out)
                else PlanAction.Refused (2, err "cli.synthCorrect.noModel" "projection synth-correct: no model is configured (set `model` or `model.ossys` in projection.json) — the correction is proposed from the model's catalog.")
        { Notes = []; Action = action }

    /// The closed secondary-verb set (THE_CLI.md §3). A first token outside
    /// this set is read as a flow name; an unknown one is refused, naming both.
    /// NM-10 — single-sourced from `ProjectionConfig.reservedFlowVerbs` (the
    /// config-load collision check rejects a flow named after any of these), so
    /// the router and the load-time refusal can never drift apart.
    let private secondaryVerbs = ProjectionConfig.reservedFlowVerbs

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
        | "compare" :: rest -> Result.success (Intent.Compare rest)
        | "revert" :: rest  -> Result.success (Intent.Revert rest)
        | "profile" :: rest -> Result.success (Intent.Profile rest)
        | "synth-correct" :: rest -> Result.success (Intent.SynthCorrect rest)
        // Slice data-portability verbs (recon #3) — formerly dispatched on a raw
        // `argv.[0]` match in `Program.main`, now first-class typed intents on the
        // one dispatch plane. `slice-reset` is `slice-apply` under `reset = true`.
        | "slice-extract" :: rest -> Result.success (Intent.SliceExtract rest)
        | "slice-apply" :: rest   -> Result.success (Intent.SliceApply (false, rest))
        | "slice-reset" :: rest   -> Result.success (Intent.SliceApply (true, rest))
        | "slice-run" :: rest     -> Result.success (Intent.SliceFlow rest)
        | first :: rest when Map.containsKey first cfg.Flows ->
            // D8 — the value-bearing synthesis knobs. A malformed value is a
            // refusal (named, never a silent fall-through to the default).
            let seedR : Result<uint64 option> =
                match flagValue rest "--seed" with
                | None -> Result.success None
                | Some raw ->
                    match System.UInt64.TryParse raw with
                    | true, v -> Result.success (Some v)
                    | _ -> Result.failureOf (err "cli.flow.seedInvalid" (sprintf "--seed '%s' is not a non-negative integer." raw))
            let scaleR : Result<decimal option> =
                match flagValue rest "--scale" with
                | None -> Result.success None
                | Some raw ->
                    match System.Decimal.TryParse(raw, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture) with
                    | true, v when v > 0M -> Result.success (Some v)
                    | true, _ -> Result.failureOf (err "cli.flow.scaleInvalid" (sprintf "--scale '%s' must be greater than zero." raw))
                    | _ -> Result.failureOf (err "cli.flow.scaleInvalid" (sprintf "--scale '%s' is not a number." raw))
            match seedR, scaleR with
            | Error es, _ | _, Error es -> Result.failure es
            | Ok seed, Ok scale ->
                let opts =
                    { Go         = List.contains "--go" rest
                      Fresh      = List.contains "--fresh" rest
                      AllowDrops = List.contains "--allow-drops" rest
                      AllowCdc   = List.contains "--allow-cdc" rest
                      Resumable  = List.contains "--resumable" rest
                      Streaming  = List.contains "--streaming" rest
                      Journal    = flagValue rest "--journal"
                      NoAtomic   = List.contains "--no-atomic" rest
                      AutoRevert = List.contains "--auto-revert" rest
                      RevertDir  = flagValue rest "--revert-dir"
                      Seed       = seed
                      Scale      = scale
                      Correction = flagValue rest "--correction" }
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
        | Intent.Compare args      -> planCompare cfg args
        | Intent.Revert args       -> planRevert cfg args
        | Intent.Profile args      -> planProfile cfg args
        | Intent.SynthCorrect args -> planSynthCorrect cfg args
        | Intent.SliceExtract args         -> { Notes = []; Action = PlanAction.RunSliceExtract args }
        | Intent.SliceApply (reset, args)  -> { Notes = []; Action = PlanAction.RunSliceApply (reset, args) }
        | Intent.SliceFlow args            -> { Notes = []; Action = PlanAction.RunSliceFlow args }
