namespace Projection.Pipeline

open Projection.Core

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
        /// Durable provenance store; fills from the target's config or `--store`.
        Store       : string option
        /// Environment label for the timeline / episode.
        Env         : string option
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
            Store       = None
            Env         = None
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
        Store       : string option
        Env         : string option
        /// Declared table subset for the data leg (item 5); empty = all.
        Tables      : string list
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
    /// `DataOrigin` alone (both are `FromTarget`). The runner needs two
    /// SsKey-aligned contracts (logical source + physical sink of the ONE model);
    /// a live two-DB flow cannot produce them yet (the J3 residual,
    /// THE_DATA_PRODUCERS §6 LE-1), so the runner resolves to a NAMED REFUSAL
    /// (`cli.move.reverseLegResidual`) rather than mis-running as a peer transfer.
    | RunReverseLeg of source: string * sink: string * opts: LoadOpts * execute: bool
    /// live + synthetic data source → generate from the durable profile and
    /// load (DryRun preview when execute=false; the DML-only load when
    /// execute=true). The model supplies the target schema B — read live from
    /// OSSYS when `modelOssys` is set (primary; V1-free) else from the model
    /// file (fallback); the profile ref (`file:<path>`) supplies the evidence σ
    /// replays.
    | SynthesizeAndLoad of model: ModelSource * modelOssys: string option * profile: string * conn: string * opts: LoadOpts * execute: bool
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
    | ExplainPolicy of configA: string * configB: string
    | ExplainNode of config: string * ssKey: string
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
    /// the flow's target durable timeline (THE_CLI.md §8 / F4).
    | ReportBundle of store: string
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
