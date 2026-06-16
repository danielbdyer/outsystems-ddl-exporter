namespace Projection.Pipeline

open Projection.Core

/// M23 — the data-leg revert policy a sink environment carries (`"revert"` in
/// `projection.json`). On a failed data load: `Script` (the safe default) writes
/// the precise `DELETE`-by-captured-key revert .sql to an artifact for the
/// operator to review/run; `Auto` executes it immediately (the `--auto-revert`
/// arm); `Off` does neither (re-run is the recovery). A per-environment
/// disposition — config-resident, with a per-run CLI override. At the engine
/// boundary it collapses to `WriteOptions.(AutoRevert, RevertArtifactDir)`.
[<RequireQualifiedAccess>]
type RevertPolicy =
    | Script
    | Auto
    | Off

[<RequireQualifiedAccess>]
module RevertPolicy =
    /// The default when an environment names no `revert` policy: emit the script
    /// (never auto-delete; never silently skip) — the safe, operator-reviewable arm.
    let def : RevertPolicy = RevertPolicy.Script

    /// Parse the `"revert"` config token. Total + fail-loud on an unknown token.
    let tryParse (raw: string) : Result<RevertPolicy, string> =
        match raw.Trim().ToLowerInvariant() with
        | "script" -> Ok RevertPolicy.Script
        | "auto"   -> Ok RevertPolicy.Auto
        | "off"    -> Ok RevertPolicy.Off
        | other    -> Error (sprintf "unknown revert policy '%s' (expected: script | auto | off)" other)

    /// The canonical token (the render dual of `tryParse`; `tryParse ∘ token = id`).
    let token (p: RevertPolicy) : string =
        match p with
        | RevertPolicy.Script -> "script"
        | RevertPolicy.Auto   -> "auto"
        | RevertPolicy.Off    -> "off"

    /// Collapse a policy + resolved artifact dir to the engine's two write
    /// levers: `(autoRevert, revertArtifactDir)`. `Auto` executes AND records the
    /// script; `Script` records only; `Off` neither.
    let toEngine (dir: string option) (p: RevertPolicy) : bool * string option =
        match p with
        | RevertPolicy.Auto   -> true,  dir
        | RevertPolicy.Script -> false, dir
        | RevertPolicy.Off    -> false, None

// THE_CLI.md §13 — the typed surface the four operator verbs project onto.
// One `MovementSpec` is the whole emission family: every current verb
// (emit / skeleton / deploy / full-export / transfer / migrate) is a point
// in this space, distinguished by its axes. The engine that consumes a
// `MovementSpec` is `emit(B ⊖ A)` specialized; the verbs are thin faces.
// Types only here — no I/O, no behavior beyond the defaults that vanish.

/// The destination a projection lands at — the first axis the operator
/// names (THE_CLI.md §3). The destination decides the realization form.
[<RequireQualifiedAccess>]
type Destination =
    /// A folder on disk → the file bundle (publication; A = ∅).
    | Folder of path: string
    /// An ephemeral one-touch database, deployed and verified.
    | Docker
    /// A live database, addressed out-of-band (D9) → read A, apply B ⊖ A.
    | Live of ConnectionRef

/// Which legs of the T16 square the movement emits — the operator's
/// permission scope (DDL+DML vs DML-only). Schema and data are the two
/// projections of the master equation.
[<RequireQualifiedAccess>]
type Scope =
    | All
    | Schema
    | Data

/// The data-plane replacement strategy — the norm-shaping axis (T15).
/// `Merge` is isometric (CDC-silent on a zero delta); `Replace` is the
/// non-isometric wipe-and-load fallback; `Fresh` is genesis-load.
[<RequireQualifiedAccess>]
type Strategy =
    | Merge
    | Replace
    | Fresh

/// Where the rows come from. `FromTarget` (an alias of another live
/// target) is the DB→DB transfer ingest, folded onto this one axis.
[<RequireQualifiedAccess>]
type DataOrigin =
    | Model
    /// Generated to match a profile (THE_SYNTHETIC_DATA_DESIGN). Carries the
    /// durable profile reference (`file:<path>`) — the evidence σ replays.
    | Synthetic of profile: string
    | NoData
    | FromTarget of alias: string

/// The baseline A in (B ⊖ A). `Auto` is ∅ for a folder and the
/// destination's current state for a live target — the auto-A principle
/// that collapses deploy / migrate / redeploy into one act.
[<RequireQualifiedAccess>]
type Baseline =
    | Auto
    | Empty
    | FromModel of path: string
    | FromTarget of alias: string

/// File-bundle composition (folder destinations only).
[<RequireQualifiedAccess>]
type Shape =
    | Bundle
    | Ssdt
    | Skeleton
    /// The applied-transforms manifest alone (`manifest.json`), without the
    /// bundle siblings — the registry's per-kind self-description as a
    /// standalone artifact (A-cluster manifest exposure).
    | Manifest

/// Where the projected state B is authored. A bare model file (the authored
/// `Catalog`), the unified config (which carries the model path plus the
/// operator overlays), or unspecified — resolved from `projection.json`'s
/// `model` field, else refused. THE_CLI.md §3: "one input — the model."
[<RequireQualifiedAccess>]
type ModelSource =
    | ModelFile of path: string
    | ConfigFile of path: string
    | Unspecified

/// The DERIVED direction of a movement (G2) — a *binding*, never a stored or
/// parsed knob (THE_DATA_PRODUCERS §286-289; THE_CONFIG_CONTROL_PLANE §2/§3).
/// Computed in `resolveFlowSpec` from `(sourceRendition, sinkRendition, scope)`:
/// `Down` is the A→B down-leg (model → bundle/live; the established publish
/// path); `UpSynthetic` is the mint→A synthetic insertion; `UpPeer` is the A→A
/// physical→physical peer/golden re-key; `UpLegacy` is the B→A legacy reverse
/// leg (logical source → physical sink — the leg the engine must route distinctly
/// from a peer transfer). Closed so a renderer / router is total over it.
[<RequireQualifiedAccess>]
type MovementDirection =
    | Down
    | UpSynthetic
    | UpPeer
    | UpLegacy

/// The fully-resolved movement — what the one engine consumes. Every
/// today-verb is a point in this space (THE_CLI.md §7). `Commit` is the
/// operator's intent (`--go`); the `PROJECTION_ALLOW_EXECUTE` env var is
/// the environment's authorization — a live write needs both.
type MovementSpec =
    {
        Destination : Destination
        Model       : ModelSource
        Baseline    : Baseline
        Scope       : Scope
        /// G2 — the DERIVED direction (a binding from renditions + scope, never
        /// parsed). `planMovement` routes `UpLegacy` to the reverse-leg runner;
        /// every other direction rides the established routing. Default `Down`
        /// (the publish/down-leg — byte-identical to the prior behavior).
        Direction   : MovementDirection
        Strategy    : Strategy
        Data        : DataOrigin
        Shape       : Shape
        /// Path to the user-map CSV (Reidentify); `None` is no re-key.
        Rekey       : string option
        /// `--reconcile <table>:<col>` entries (MatchByColumn re-key rules).
        Reconcile   : string list
        /// Declared table subset for the data leg (golden data); empty = all.
        Tables      : string list
        /// Accept declared loss (drops) — never sourced from config (§4).
        AllowDrops  : bool
        /// Permit schema DDL against a CDC-tracked sink.
        AllowCdc    : bool
        /// G10 — route the incremental data load through the resumable /
        /// idempotent-upsert envelope (`Transfer.runResumable`). Honored on the
        /// pure-transfer data leg; inert elsewhere. Default false (byte-identical).
        Resumable   : bool
        /// The streaming realization flag (reverse leg only; `--streaming`).
        Streaming   : bool
        /// The chunk-resume journal directory (streaming only; `--journal`).
        Journal     : string option
        /// M22 — the RESOLVED atomic schema-deploy disposition (derived: ON for a
        /// direct full-access sink; env `atomicDeploy` + `--no-atomic` override).
        Atomic      : bool
        /// M23 — the RESOLVED data-leg revert policy (the sink's `revert`, or the
        /// `--auto-revert` override, or the `Script` default).
        RevertPolicy : RevertPolicy
        /// M23 — the revert artifact dir override (`--revert-dir`); `None` derives.
        RevertDir   : string option
        /// D8 — the synthesis PRNG seed (`--seed <n>`). Honored on the synthetic
        /// load; inert elsewhere. `None` = the fixed default seed.
        Seed        : uint64 option
        /// D8 — the synthesis volume factor (`--scale <f>`). Honored on the
        /// synthetic load; inert elsewhere. `None` = full scale (1.0).
        Scale       : decimal option
        /// Durable provenance store; fills from the target's config or `--store`.
        Store       : string option
        /// Environment label for the timeline / episode.
        Env         : string option
        /// Slice C — the sink's capability-derived engine inputs, set by
        /// `resolveFlowSpec` from the target environment's effective `Archetype`.
        /// `structural` default (byte-identical); only a `FullRights` sink forks.
        SinkCapability : SinkLoadCapability
        Commit      : bool
    }

[<RequireQualifiedAccess>]
module MovementSpec =

    /// The defaults that vanish (THE_CLI.md §3.1): all legs, merge,
    /// model data, the full bundle, auto baseline, preview (not committed).
    let forDestination (destination: Destination) : MovementSpec =
        {
            Destination = destination
            Model       = ModelSource.Unspecified
            Baseline    = Baseline.Auto
            Scope       = Scope.All
            Direction   = MovementDirection.Down
            Strategy    = Strategy.Merge
            Data        = DataOrigin.Model
            Shape       = Shape.Bundle
            Rekey       = None
            Reconcile   = []
            Tables      = []
            AllowDrops  = false
            AllowCdc    = false
            Resumable   = false
            Streaming   = false
            Journal     = None
            Atomic      = false
            RevertPolicy = RevertPolicy.def
            RevertDir   = None
            Seed        = None
            Scale       = None
            Store       = None
            Env         = None
            SinkCapability = SinkLoadCapability.structural
            Commit      = false
        }

    /// Whether a live write would occur — a folder is always a safe
    /// produce; Docker is ephemeral; a `Live` write occurs only when
    /// committed. The two-gate rule (intent × authorization) is enforced
    /// at the boundary, not here.
    let isLiveWrite (spec: MovementSpec) : bool =
        match spec.Destination with
        | Destination.Live _ -> spec.Commit
        | Destination.Folder _ | Destination.Docker -> false

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
/// table subset). The named recipe the daily command runs.
type Flow =
    {
        Name   : string
        From   : FlowSource
        To     : string
        Rekey  : string option
        Tables : string list
        /// J2 — per-flow `MatchByColumn` re-key rules ("<table>:<col>" each;
        /// e.g. the golden flow's `"reconcile": ["OSSYS_USER:EMAIL"]` so the
        /// declarative flow carries the User re-key without a per-run tail).
        /// Empty = no reconcile (byte-identical).
        Reconcile : string list
        /// The move's PROJECTION (G1): which legs of the T16 square THIS move
        /// carries — the schema leg, the data leg, or both. Decoupled from the
        /// target's `grant` (the refusal gate, what MAY change there). `None`
        /// = the grant-derived default (back-compat: a `data`-granting target
        /// implies `Scope.Data`, else `Scope.All`). Resolved in `resolveFlowSpec`.
        Scope  : Scope option
        /// The bundle composition this move emits (S6.1) — `Bundle` / `Ssdt` /
        /// `Skeleton`. `None` = the `Bundle` default (a folder model flow emits
        /// the full pass-chain bundle; `skeleton` selects the pre-overlay emit).
        /// Resolved in `resolveFlowSpec` to `MovementSpec.Shape`.
        Shape  : Shape option
        /// An opt-in per-flow `shaping` override (S6.4 — "global + opt-in per-flow
        /// override"). `None` = use the global `cfg.Shaping` (byte-identical);
        /// `Some` deep-overlays the global at whole-section granularity
        /// (`Config.overlay`) for THIS flow's emission only. Parsed from a nested
        /// `"shaping"` object via `Config.parseLenient`.
        Shaping : Config.Config option
    }

/// The per-run intent that finishes a resolved flow (THE_CLI.md §3) — the
/// only words that vary at the moment of action and never live in config.
type FlowRunOpts =
    {
        Go         : bool
        Fresh      : bool
        AllowDrops : bool
        /// Override the CDC-tracked-sink pre-flight gate (the gate refuses a
        /// live write into a CDC-tracked sink unless this is set — item 3).
        AllowCdc   : bool
        /// `--resumable` — route the incremental data load through the resumable
        /// / idempotent-upsert envelope (G10). Default false (byte-identical).
        Resumable  : bool
        /// `--seed <n>` — the synthesis PRNG seed (D8; THE_SYNTHETIC_DATA_DESIGN
        /// §7). `None` = the fixed default seed (byte-identical reproducibility).
        Seed       : uint64 option
        /// `--scale <f>` — the synthesis volume factor over the profiled
        /// `RowCount` per kind (D8; design §7). `None` = full scale (1.0).
        Scale      : decimal option
        /// `--streaming` — the bounded-memory chunked realization for the
        /// estate-scale reverse leg (the hundreds-of-millions-row program). Honored on the
        /// B→A reverse leg only; the face refuses unsupported combinations
        /// by name. Default false (byte-identical).
        Streaming  : bool
        /// `--journal <dir>` — the chunk-resume journal directory for a
        /// streaming run (`CaptureJournal`). `None` = no resume ledger.
        Journal    : string option
        /// M22 — `--no-atomic`: opt OUT of the atomic schema-deploy envelope per
        /// run (atomic is ON by default for a direct full-access sink — derived
        /// from the archetype, overridable in config). Default false (= atomic on).
        NoAtomic   : bool
        /// M23 — `--auto-revert`: force the data-leg revert policy to `Auto`
        /// (execute the compensating DELETE-by-captured-key) for this run,
        /// overriding the sink's configured `revert`. Default false.
        AutoRevert : bool
        /// M23 — `--revert-dir <dir>`: override where the revert script is written
        /// on a failed load. `None` = derive from the run artifact dir.
        RevertDir  : string option
    }

/// The operator intents (THE_CLI.md §2). `Flow` is the hero — the daily
/// `projection <flow>` act. Check/Explain/Seal/Report carry their raw tail;
/// their typed sub-surfaces land in their build slices. (The `MovementSpec`
/// a flow resolves to is routed by `Command.planMovement`; there is no raw
/// `project --to` intent — that surface was removed at F5.)
[<RequireQualifiedAccess>]
type Intent =
    | Flow of flow: Flow * opts: FlowRunOpts
    | Check of args: string list
    | Explain of args: string list
    | Seal of args: string list
    | Report of args: string list
    /// `profile <env> --out <path>` — capture the durable Profile artifact
    /// (THE_SYNTHETIC_DATA_DESIGN §2.2). The capture step the synthetic flow
    /// replays from.
    | Profile of args: string list
    /// `compare <A> <B>` — NM-71/WP9: the read-only multi-environment readiness
    /// check (schema delta + data dealbreakers). Advisory; no writes.
    | Compare of args: string list

/// The spec-derived options a live load/migrate carries, bundled so the plan
/// is self-contained (the runner needs nothing but the plan).
type LoadOpts =
    {
        Declaration : LossDeclaration
        Emission    : EmissionMode
        Reconcile   : string list
        Rekey       : string option
        AllowCdc    : bool
        /// G10 — resumable/idempotent data load on the pure-transfer leg.
        Resumable   : bool
        /// The streaming realization (reverse leg only).
        Streaming   : bool
        /// The chunk-resume journal directory (streaming only).
        Journal     : string option
        /// M22 — the resolved atomic schema-deploy disposition (see `MovementSpec.Atomic`).
        Atomic      : bool
        /// M23 — the resolved data-leg revert policy (see `MovementSpec.RevertPolicy`).
        RevertPolicy : RevertPolicy
        /// M23 — the revert artifact dir override (`--revert-dir`); `None` derives.
        RevertDir   : string option
        Store       : string option
        Env         : string option
        /// Declared table subset for the data leg (item 5); empty = all.
        Tables      : string list
        /// D8 — the synthesis PRNG seed; honored on the synthetic load only.
        Seed        : uint64 option
        /// D8 — the synthesis volume factor; honored on the synthetic load only.
        Scale       : decimal option
        /// Slice C — the sink's capability-derived engine inputs (the identity
        /// policy + sink-resident-resume availability), projected from the sink
        /// `Environment`'s effective `Archetype` at flow resolution. `structural`
        /// = the byte-identical default (a ManagedDml or undeclared sink).
        SinkCapability : SinkLoadCapability
    }

/// The engine face a parsed `Intent` routes to, named with the cfg-resolved
/// arguments it needs — the **pure, testable seam** spanning all four verbs
/// (the CLI's `registered ⇔ executed`). Planning is pure (a `ConfigFile`
/// carries its path; the runner resolves it); the routing is totality-tested,
/// `Refused` (a coded `ValidationError` + exit) included — total decisions,
/// named skips. The runner (`runPlan`) executes the action against the proven
/// `run*` faces and voices every `Refused` through `Voice.errorSurface`.
[<RequireQualifiedAccess>]
type PlanAction =
    // project ------------------------------------------------------------
    // The model-bearing actions carry `model` (the osm_model.json fallback as a
    // `ModelSource`) + `modelOssys` (the live-OSSYS primary, when configured);
    // the runner resolves via `ModelResolution.resolveCatalog`.
    /// folder + config → the full-export bundle (richer than a bare emit).
    | PublishBundle of config: string * dir: string * store: string option * env: string option
    /// folder + model + skeleton shape → the pre-overlay emit.
    | EmitSkeleton of model: ModelSource * modelOssys: string option * dir: string
    /// folder + model + manifest shape → the applied-transforms manifest
    /// alone (the full chain runs; only `manifest.json` is written).
    | EmitManifest of model: ModelSource * modelOssys: string option * dir: string
    /// folder + model + bundle/ssdt shape → the full pass-chain emit.
    | EmitBundle of model: ModelSource * modelOssys: string option * dir: string
    /// docker → one-touch ephemeral deploy.
    | DeployDocker of model: ModelSource * modelOssys: string option
    /// live, no --go, no data source → the schema plan preview (B ⊖ A).
    | PreviewSchema of model: ModelSource * modelOssys: string option * conn: string * declaration: LossDeclaration
    /// live + data source → transfer (DryRun preview when execute=false; the
    /// DML-only load when execute=true under --scope data).
    | Transfer of source: string * sink: string * opts: LoadOpts * execute: bool
    /// live + data source whose DERIVED direction is `UpLegacy` (B→A) → the
    /// reverse-leg runner (`Transfer.runReverseLeg` / the M3.b face). The engine
    /// distinguishes this from an A→A peer `Transfer` — which it cannot do by
    /// `DataOrigin` alone (both are `FromTarget`). The runner's two SsKey-aligned
    /// contracts (logical source + physical sink) are the ONE authored model
    /// RENDERED at both renditions (`CatalogRendition`; J3 closed) — so the
    /// action carries the model the way `PreviewSchema`/`Migrate` do, and a
    /// model-less legacy flow refuses at PLAN time (named), never at the runner.
    | RunReverseLeg of model: ModelSource * modelOssys: string option * source: string * sink: string * opts: LoadOpts * execute: bool
    /// live + synthetic data source → generate from the durable profile and
    /// load (DryRun preview when execute=false; the DML-only load when
    /// execute=true). The model supplies the target schema B — read live from
    /// OSSYS when `modelOssys` is set (primary; V1-free) else from the model
    /// file (fallback); the profile ref (`file:<path>`) supplies the evidence σ
    /// replays.
    ///
    /// NM-08/09 — carries the `modelSection` (the config's `model` block) so the
    /// resolved synthetic catalog passes through the SAME module-filter seam
    /// (`ModuleFilterBinding.fromConfig` → `ModuleFilter.apply`) every other
    /// action routes through at `Program.needCatalog`. Without it a `from:
    /// synthetic` flow emitted the FULL estate, silently ignoring `model.modules`.
    /// An empty `model.modules` is the all-permissive identity, so the default
    /// synthetic load stays byte-identical.
    | SynthesizeAndLoad of model: ModelSource * modelOssys: string option * profile: string * conn: string * opts: LoadOpts * execute: bool * modelSection: Config.ModelSection
    /// live, --go, data source → cross-substrate migrate-with-data.
    | MigrateWithData of model: ModelSource * modelOssys: string option * sink: string * source: string * opts: LoadOpts
    /// live, --go, config model → publish bundle + load the seed.
    | PublishAndLoad of config: string * conn: string * store: string option * env: string option
    /// live, --go, bare model → in-place schema migrate.
    | Migrate of model: ModelSource * modelOssys: string option * conn: string * opts: LoadOpts
    // check --------------------------------------------------------------
    | CheckCanary of ddl: string * cdcSilence: bool
    | CheckDrift of model: string * conn: string
    | CheckData of before: string * after: string
    | CheckReady
    // explain ------------------------------------------------------------
    | ExplainDiff of refA: string * refB: string * asJson: bool * depth: int option
    | Compare of refA: string * refB: string * asJson: bool
    | ExplainPolicy of configA: string * configB: string
    | ExplainNode of config: string * ssKey: string * asJson: bool * depth: int option
    | ExplainSuggest of config: string * applyTo: string option
    | ExplainRegistry
    | ExplainMigratePreview of fromPath: string * toPath: string * declaration: LossDeclaration
    /// `forceGenesis` (the `--from empty` flag): when true, the prior state A is
    /// forced to ∅ (every kind `Add`, no losses) EVEN when the store file exists —
    /// the "first emission" framing on demand. Default false preserves the
    /// store-derived A (genesis only when the store is absent).
    | ExplainMigrateFromStore of store: string * toPath: string * declaration: LossDeclaration * forceGenesis: bool
    // seal ---------------------------------------------------------------
    | SealEject of store: string
    | SealApprove of version: string * approver: string * rationale: string option * store: string option
    // report -------------------------------------------------------------
    /// the migration-team change bundle: the ChangeManifest series read from
    /// the flow's target durable timeline (THE_CLI.md §8 / F4). `outputDir` is
    /// the flow target's bundle `out` folder (when one is configured) — the
    /// directory the full-export that fed this timeline wrote `fidelity.json`
    /// into, so the report verb can surface the recorded Model Fidelity Report
    /// without guessing. `None` for a `--store`-only report (no flow env) or a
    /// non-bundle target.
    | ReportBundle of store: string * outputDir: string option
    // profile ------------------------------------------------------------
    /// capture the durable Profile from a live environment to a file
    /// (THE_SYNTHETIC_DATA_DESIGN §2.2): read → profile → serialize.
    | CaptureProfile of conn: string * out: string
    // shared -------------------------------------------------------------
    /// a named refusal — a coded `ValidationError` (voiced) + its exit code.
    | Refused of exit: int * error: ValidationError

/// A planned execution: the unhonored-axis notes (surfaced, never dropped —
/// fidelity #2) plus the routed action.
type ExecutionPlan =
    {
        Notes  : string list
        Action : PlanAction
    }
