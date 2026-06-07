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
    | Synthetic
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

/// The four operator intents (THE_CLI.md §2). `Project` is the hero
/// (all data movement); the other three are the proof, the understanding,
/// and the provenance planes. Check/Explain/Seal carry their raw tail in
/// this skeleton — their typed sub-surfaces land in their build slices.
[<RequireQualifiedAccess>]
type Intent =
    | Project of MovementSpec
    | Check of args: string list
    | Explain of args: string list
    | Seal of args: string list
