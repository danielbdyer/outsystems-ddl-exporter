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
            Strategy    = Strategy.Merge
            Data        = DataOrigin.Model
            Shape       = Shape.Bundle
            Rekey       = None
            Reconcile   = []
            Tables      = []
            AllowDrops  = false
            AllowCdc    = false
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

/// The spec-derived options a live load/migrate carries, bundled so the plan
/// is self-contained (the runner needs nothing but the plan).
type LoadOpts =
    {
        Declaration : LossDeclaration
        Emission    : EmissionMode
        Reconcile   : string list
        Rekey       : string option
        AllowCdc    : bool
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
    /// folder + config → the full-export bundle (richer than a bare emit).
    | PublishBundle of config: string * dir: string * store: string option * env: string option
    /// folder + model + skeleton shape → the pre-overlay emit.
    | EmitSkeleton of model: string * dir: string
    /// folder + model + bundle/ssdt shape → the full pass-chain emit.
    | EmitBundle of model: string * dir: string
    /// docker → one-touch ephemeral deploy (runner resolves the model).
    | DeployDocker of model: ModelSource
    /// live, no --go, no data source → the schema plan preview (B ⊖ A).
    | PreviewSchema of model: ModelSource * conn: string * declaration: LossDeclaration
    /// live + data source → transfer (DryRun preview when execute=false; the
    /// DML-only load when execute=true under --scope data).
    | Transfer of source: string * sink: string * opts: LoadOpts * execute: bool
    /// live + synthetic data source → generate from the durable profile and
    /// load (DryRun preview when execute=false; the DML-only load when
    /// execute=true). The model supplies the target schema B; the profile ref
    /// (`file:<path>`) supplies the evidence σ replays.
    | SynthesizeAndLoad of model: ModelSource * profile: string * conn: string * opts: LoadOpts * execute: bool
    /// live, --go, data source → cross-substrate migrate-with-data.
    | MigrateWithData of model: ModelSource * sink: string * source: string * opts: LoadOpts
    /// live, --go, config model → publish bundle + load the seed.
    | PublishAndLoad of config: string * conn: string * store: string option * env: string option
    /// live, --go, bare model → in-place schema migrate.
    | Migrate of model: ModelSource * conn: string * opts: LoadOpts
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
    | ExplainMigrateFromStore of store: string * toPath: string * declaration: LossDeclaration
    // seal ---------------------------------------------------------------
    | SealEject of store: string
    | SealApprove of version: string * approver: string * rationale: string option * store: string option
    // report -------------------------------------------------------------
    /// the migration-team change bundle: the ChangeManifest series read from
    /// the flow's target durable timeline (THE_CLI.md §8 / F4).
    | ReportBundle of store: string
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
