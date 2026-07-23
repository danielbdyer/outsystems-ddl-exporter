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
    /// A folder on disk → the transferred subset's DATA as CSV files
    /// (2026-07-10, the csv-destination program): one file per table plus
    /// export-manifest.json. Read-only against every database; A = ∅.
    | Csv of dir: string

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
        /// 2026-07-08 — the flow's `reconcileIgnore` audit columns (shared
        /// attribute names the matched-pair diff skips).
        ReconcileIgnore : string list
        /// 2026-07-09 (T0.3) — acknowledged out-of-contract references
        /// (`OwnerKind.ReferenceName`), declared environment-stable.
        ForeignRefs : string list
        /// 2026-07-09 — the peer contracts' IDENTITY BASIS (`by-sskey` default /
        /// `by-name` for cloned modules). `ByName` runs the name-alignment pass.
        Alignment : AlignmentMode
        /// 2026-07-09 — the cloned-module source→sink MODULE correspondence
        /// (`by-name` only): source-module-name → sink-module-name.
        AlignMap : Map<string, string>
        /// 2026-07-08 — the typed supporting-scope vocabulary (owned children,
        /// references, anchors, lookups, blocked dependents). Empty = none.
        SupportingScope : SupportingScope.SupportingScopeEntry list
        /// 2026-07-08 — the per-flow write-signoff greenlight (the destructive
        /// modes the operator approved). Empty = nothing greenlit.
        Signoff     : WriteSignoff.WriteApproval list
        /// 2026-07-10 (slice 4a) — the per-ACT blessings: each names one act
        /// token at one exact fingerprint. Empty = no act blessed.
        ActSignoff  : WriteSignoff.ActBlessing list
        /// 2026-07-10 (the csv-destination program) — the csv export also
        /// pulls the referenced non-static tables, transitively closed.
        WithReferenced : bool
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
        /// The journal directory (`--journal`): the streaming realization's
        /// chunk-resume ledger, and — wave B4a, "prove implies journal" — the
        /// materialized realization's provenance record of every captured
        /// `(source → assigned)` pair (the fidelity proof's intervention ledger).
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
        /// F0c-I/O (FUZZING §2) — the blessed correction-artifact reference
        /// (`file:<path>`), resolved from the flow's `correction` field or the
        /// `--correction` per-run override. Honored on the synthetic load (it
        /// threads `Profile ⊕ Correction` into σ AND drives the boundary Faker
        /// realization); inert elsewhere. `None` = the faithful section.
        Correction  : string option
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
            ReconcileIgnore = []
            ForeignRefs = []
            Alignment   = AlignmentMode.BySsKey
            AlignMap    = Map.empty
            SupportingScope = []
            Signoff     = []
            ActSignoff  = []
            WithReferenced = false
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
            Correction  = None
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
        | Destination.Folder _ | Destination.Docker | Destination.Csv _ -> false

/// Where a flow's content originates (THE_CLI.md §4.2): another environment
/// (the cross-substrate Move), the authored model's own data, profiled
/// synthetic data, or no data (schema only).
[<RequireQualifiedAccess>]
type FlowSource =
    | Env of env: string
    | Model
    /// Generated to match a captured profile (THE_SYNTHETIC_DATA_DESIGN). The
    /// optional `correction` (FUZZING §2 / slice F0c-I/O) is the durable
    /// blessed-correction artifact reference (`file:<path>`) — the operator's
    /// named, closed departures from naive fidelity (PII typing → Faker
    /// realization, fidelity overrides, volume). `None` = no correction (σ stays
    /// the faithful section; byte-identical to the pre-F0c flow).
    | Synthetic of profile: string option * correction: string option
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
        /// 2026-07-08 — shared attribute names the reconciled-kind
        /// matched-pair diff IGNORES (the audit fields: CreatedOn /
        /// UpdatedOn). One global list beside `reconcile`, applied to
        /// every reconciled kind. Empty = diff every non-key column.
        ReconcileIgnore : string list
        /// 2026-07-09 (T0.3) — acknowledged OUT-OF-CONTRACT references
        /// (`OwnerKind.ReferenceName` each), declared environment-stable so the
        /// engine's `subsetForeignRefsGate` does not refuse them.
        ForeignRefs : string list
        /// 2026-07-09 — the peer contracts' IDENTITY BASIS. `by-sskey` (default)
        /// = renditions of one model / GUID-stable read. `by-name` = CLONED
        /// modules (same-named entities, distinct native GUIDs) — the peer face
        /// runs `NameAlignment.align` over `AlignMap` before the SsKey gates.
        Alignment : AlignmentMode
        /// 2026-07-09 — the cloned-module source→sink MODULE correspondence,
        /// honored only under `by-name`: source-module-name → sink-module-name.
        AlignMap : Map<string, string>
        /// 2026-07-08 (the business-intent program) — the typed vocabulary
        /// for the SUPPORTING (non-payload) rows a partial transfer touches:
        /// owned children, seeded/matched references, shared anchors, static
        /// lookups, and deliberately-blocked dependents. Each entry declares
        /// WHY a table is in play and the board VERIFIES the intent against
        /// the relationship graph. Empty = no supporting scope (byte-identical);
        /// the terse `reconcile` strings resolve into the same model.
        SupportingScope : SupportingScope.SupportingScopeEntry list
        /// 2026-07-08 (the greenlight program) — the per-flow WRITE SIGNOFF: the
        /// destructive write modes (replace / fresh / drops / cdc / identity-insert /
        /// delete-scope) the operator has EXPLICITLY approved for this flow, each
        /// with its acknowledged impact + optional table scope. A destructive live
        /// run is REFUSED (and the go board reds) until the mode it performs is
        /// greenlit here — an authorization predicate beside `PROJECTION_ALLOW_EXECUTE`
        /// / `--go`, but durable and auditable. Empty = nothing greenlit (a
        /// destructive flow will not run until it declares one).
        Signoff : WriteSignoff.WriteApproval list
        /// 2026-07-10 (the transfer-manifest program, slice 4a) — the per-ACT
        /// blessings the same `signoff` array carries: each entry names one
        /// canonical act token (`ActConsent.tokenOf`) at one exact captured
        /// fingerprint, so consent attaches to the individual destructive /
        /// creative act at the substrate the operator actually read. Empty =
        /// no act blessed. Parsed from `{ "act": …, "fingerprint": … }`
        /// entries beside the `{ "mode": … }` mode approvals.
        ActSignoff : WriteSignoff.ActBlessing list
        /// 2026-07-10 (the csv-destination program) — pull the rows of
        /// referenced NON-STATIC tables into a csv export, transitively
        /// closed (static reference tables are skipped — their content is
        /// environment-identical by declaration). Honored on the csv-export
        /// leg only; default false.
        WithReferenced : bool
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
        // AUDIT (config-primary) — the flow's declared EXECUTION PROFILE, so the
        // flow is a complete recipe and the daily run collapses to `projection
        // <flow> [--go]`. Each is the DECLARATIVE baseline; the matching CLI flag
        // is the per-run override (kept, not deprecated). `None`/`false` = the
        // established default (byte-identical to the pre-audit behavior).
        /// The data-load strategy (`"merge"` | `"replace"` | `"fresh"`). `None` =
        /// `Merge` (the norm-minimal default); `--fresh` forces `Fresh` per run.
        Strategy : Strategy option
        /// Route the data leg through the resumable / idempotent-upsert envelope
        /// (G10). `--resumable` forces it on per run. Default false.
        Resumable : bool
        /// The bounded-memory streaming realization (the estate-scale reverse
        /// leg). `--streaming` forces it on per run. Default false.
        Streaming : bool
        /// The chunk-resume journal directory (paired with streaming).
        /// `--journal <dir>` overrides per run. `None` = no resume ledger.
        Journal : string option
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
        /// `--correction <ref>` — the per-run blessed-correction override
        /// (F0c-I/O; FUZZING §2). Overrides the flow's declared `correction`
        /// when set. `None` = the flow's declared correction (or none).
        Correction : string option
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
        /// `--with-referenced` — force the csv export's referenced pull on for
        /// this run (the flow's `withReferenced` is the durable form). Honored
        /// on the csv-export leg only. Default false.
        WithReferenced : bool
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
    /// `synth-correct --out <path>` — propose a FIRST-DRAFT blessed-correction
    /// artifact from the configured model's catalog (FUZZING §2.2, slice
    /// F0c-I/O): heuristic PII typing for the operator to review / fine-tune /
    /// BLESS. The durable sibling of `profile` — both write a reviewable hinge.
    | SynthCorrect of args: string list
    /// `compare <A> <B>` — NM-71/WP9: the read-only multi-environment readiness
    /// check (schema delta + data dealbreakers). Advisory; no writes.
    | Compare of args: string list
    /// `revert [--script <path>] --against <env> [--go]` — execute (or
    /// preview) a transfer undo/revert artifact (`transfer-undo.sql` /
    /// `transfer-revert.sql`) against a configured environment: the
    /// deliberate-undo half of the proving loop (2026-07-06). Preview is the
    /// default; a live run needs PROJECTION_ALLOW_EXECUTE=1 + --go.
    | Revert of args: string list
    /// `slice-extract` / `slice-apply` / `slice-reset` / `slice-run` — the
    /// data-portability verbs (Slice 3/7). Lifted onto the typed `Intent` surface
    /// (recon #3) so the CLI runs ONE dispatcher; the bespoke flag parsing stays
    /// in the face under the deferred-parse convention `Check`/`Explain`/`Profile`
    /// share. `slice-apply` and `slice-reset` are the one face under `reset`.
    | SliceExtract of args: string list
    | SliceApply of reset: bool * args: string list
    | SliceFlow of args: string list

/// The spec-derived options a live load/migrate carries, bundled so the plan
/// is self-contained (the runner needs nothing but the plan).
type LoadOpts =
    {
        Declaration : LossDeclaration
        Emission    : EmissionMode
        Reconcile   : string list
        /// 2026-07-08 — audit columns the reconciled-kind matched-pair
        /// diff ignores (shared attribute names; from `reconcileIgnore`).
        ReconcileIgnore : string list
        /// 2026-07-09 (T0.3) — acknowledged out-of-contract references
        /// (`OwnerKind.ReferenceName`), declared environment-stable.
        ForeignRefs : string list
        /// 2026-07-09 — the peer contracts' identity basis (`by-name` runs the
        /// cloned-module name-alignment pass at the face).
        Alignment : AlignmentMode
        /// 2026-07-09 — the cloned-module source→sink module correspondence.
        AlignMap : Map<string, string>
        /// 2026-07-08 — the typed supporting-scope vocabulary; the face
        /// desugars it onto `Tables`/`Reconcile` + the seed/exclusion sets.
        SupportingScope : SupportingScope.SupportingScopeEntry list
        /// 2026-07-08 — the per-flow write-signoff greenlight; the go board reds
        /// and the engine refuses a destructive live run until its mode is here.
        Signoff     : WriteSignoff.WriteApproval list
        /// 2026-07-10 (slice 4a) — the per-act blessings the board's consent
        /// axis verifies each derived act against (enforcement lands in 4b).
        ActSignoff  : WriteSignoff.ActBlessing list
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
        /// F0c-I/O — the blessed correction-artifact reference; honored on the
        /// synthetic load only (the runner resolves + decodes it). `None` = none.
        Correction  : string option
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
    /// live + data source whose DERIVED direction is `UpPeer` (A→A: two
    /// deployed cells of ONE model, e.g. cloud-qa → cloud-uat, whose physical
    /// `OSUSR_*` names differ per espace) → the peer SsKey-aligned transfer.
    /// Unlike the bare `Transfer` (ONE `ReadSide` contract from the source,
    /// physical names assumed identical on the sink), the peer runner reads a
    /// contract from EACH side's OSSYS metamodel (`Source.ofOssys` — native
    /// GUID identity, the espace-invariance law), gates the pair on SS_KEY-
    /// keyed shape compatibility (`transfer.peer.shapeDivergence`, exit 5) and
    /// on subset-escaping FK edges (`transfer.peer.subsetFkEscapes`, exit 9),
    /// then rides the SAME rename-aware engine the reverse leg proved
    /// (`Transfer.runReverseLegThroughConnectionsWith`) — reads with the
    /// source's physical names, writes with the sink's. 2026-07-06, the
    /// partial-transfer readiness program.
    | TransferPeer of source: string * sink: string * opts: LoadOpts * execute: bool
    /// csv destination + env data source → the read-only CSV data export
    /// (2026-07-10, the csv-destination program). No execute flag: files are
    /// the safe produce (the PublishBundle precedent) — `--go` is inert.
    /// Reads its SOURCE contract from the OSSYS metamodel (the peer face's
    /// identity basis), never from a ReadSide readback (survival rule 8:
    /// ReadSide marks everything Static, which would poison the export's
    /// static-skip).
    | TransferCsvExport of source: string * dir: string * opts: LoadOpts * withReferenced: bool
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
    | SynthesizeAndLoad of model: ModelSource * modelOssys: string option * profile: string * conn: string * opts: LoadOpts * execute: bool * modelSection: Config.ModelSection * syntheticSection: Config.SyntheticSection
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
    /// `check shape` — the espace-safe cross-environment readiness gate
    /// (CROSS_ENVIRONMENT_READINESS.md). The agreed shape (an env's OSSYS model)
    /// and the confirm set, each as (label, D9 conn-ref); the runner reads every
    /// env via OSSYS (native GUID identity) and rolls a `ReadinessReport`.
    /// `model` is the `model` section whose `modules` selection scopes each
    /// OSSYS read to the cutover module surface (excluding clone / deleted /
    /// test / system eSpaces) — the reads route through `ScopedRead`.
    | CheckShape of agreedLabel: string * agreedRef: string * confirm: (string * string) list * model: Config.ModelSection * asJson: bool
    /// `check estate` — the estate-convergence instrument
    /// (CHAPTER_ESTATE_OPEN.md; DECISIONS 2026-07-15). The unification target
    /// (the readiness block's agreed environment by default; the authored
    /// model under `--against model` — the run states which it used) plus the
    /// confirm set, each as (label, D9 conn-ref); the runner reads every
    /// environment via OSSYS (native GUID identity) and rolls an
    /// `Estate.EstateReport`. Payload reified to a record from birth (the
    /// CheckGoArgs positional-misordering lesson).
    | CheckEstate of args: CheckEstateArgs
    /// `check data --rows --before <env> --after <env> --model <ref>` — the
    /// row-fidelity proof (T17, wave B2): stream both sides in primary-key
    /// order, align the physical rendition's column names to the model's
    /// logical shape, and name every differing row by its key. Payload
    /// reified to a record from birth.
    | CheckDataRows of args: CheckDataRowsArgs
    /// `check fidelity <flow>` — THE CONTAINER PROOF (T17, wave B5): scaffold
    /// a per-run database on the local container, stand the model's physical
    /// shape up on it, load it through the flow's transfer machinery
    /// (journaled wipe-and-load, FKs re-trusted), prove the load row-faithful
    /// against the flow's live source modulo the journal's recorded
    /// interventions, and reap the stand-in. The model rides the
    /// `needCatalog` seam (live-OSSYS primary, file fallback) like every
    /// model-bearing action.
    | CheckFidelityFlow of model: ModelSource * modelOssys: string option * args: CheckFidelityFlowArgs
    /// `check fidelity --against <manifest> --target <ref>` (P2-S3) — the OFFLINE
    /// reconcile: verify a target the tool did NOT stage (a database the operator
    /// applied themselves) against a PORTABLE proof manifest, with no live source.
    /// The model rides the same `needCatalog` seam (its shape is the alignment
    /// basis; a manifest captured under a different model is refused).
    | CheckFidelityAgainst of model: ModelSource * modelOssys: string option * args: CheckFidelityAgainstArgs
    /// `revert [--script <path>] --against <env> [--go]` — execute (or
    /// preview) a transfer undo/revert artifact against a configured live
    /// environment (2026-07-06, the proving-loop program). Carries the
    /// script path, the environment label (display), the RESOLVED conn
    /// spec, and the per-run intent flag.
    | RevertScript of script: string * envLabel: string * connSpec: string * go: bool * force: bool
    /// `check go <flow> [--sql]` — THE GO BOARD (2026-07-06, the
    /// preview-engine program): the red/green go-readiness checklist for a
    /// data flow. The action carries the flow's coordinates + the PLANNED
    /// action the flow would run (the same `planFlow` derivation a real
    /// run takes, under preview opts), so the board judges exactly what
    /// `--go` would execute. Payload reified to `CheckGoArgs` (2026-07-10,
    /// the manifest program): the tuple had reached three adjacent bools —
    /// the positional-misordering trap — and the review surface adds a
    /// fourth.
    | CheckGo of args: CheckGoArgs
    /// `check plan <flow>` — THE TRANSFER PLAN (2026-07-08, the guided-wizard
    /// program): the declarative counterpart to the go board. Where `check go`
    /// verdicts readiness, `check plan` walks each transfer decision axis with its
    /// alternatives, the tradeoff each carries, and the exact config edit — the
    /// strategy space made legible. The `Plan` is built pure from the flow's
    /// current choices at parse time; the face renders it (and, on a terminal,
    /// offers to pick a branch and persist it).
    | CheckPlan of flow: string * plan: TransferPlan.Plan * asJson: bool
    // explain ------------------------------------------------------------
    | ExplainDiff of refA: string * refB: string * asJson: bool * depth: int option * channel: string option * onlyModule: string option
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
    /// propose a FIRST-DRAFT blessed-correction artifact to a file (FUZZING
    /// §2.2, slice F0c-I/O): resolve the model's catalog → `CorrectionProposer
    /// .propose` (heuristic PII typing) → `CorrectionCodec.serialize` → write.
    /// The model is read live from OSSYS when `modelOssys` is set (primary;
    /// V1-free) else from the model file. The operator reviews / edits / blesses.
    | ProposeCorrection of model: ModelSource * modelOssys: string option * out: string
    // slice (data portability) ------------------------------------------
    // The slice verbs run their own bespoke flag parsing + config resolution
    // inside the face (named slices / sliceFlows are read from projection.json at
    // run time — I/O the pure plan cannot do), so the action carries the raw
    // `args` the way `PublishBundle` carries an unresolved config path the runner
    // reads. Recon #3 — one dispatch plane, no `argv.[0]` side-channel.
    /// the use-case-scoped referential-closure extract → portable golden dataset.
    | RunSliceExtract of args: string list
    /// the additive capture-and-remap MERGE (`reset=false`) or the scoped
    /// authoritative DELETE (`reset=true`) — emitted artifact, or live under `--go`.
    | RunSliceApply of reset: bool * args: string list
    /// a named extract→apply slice flow from the `sliceFlows` block.
    | RunSliceFlow of args: string list
    // shared -------------------------------------------------------------
    /// a named refusal — a coded `ValidationError` (voiced) + its exit code.
    | Refused of exit: int * error: ValidationError

/// The go board's coordinates (the `CheckGo` payload, reified 2026-07-10).
/// `Planned` is the SAME `planFlow` derivation a real run takes, so the board
/// judges exactly what `--go` would execute. `Review` opens the interactive
/// review workbench on a real terminal (the manifest program; a piped
/// `--review` degrades to the one-shot declarative render).
and CheckGoArgs =
    { Flow       : string
      FromLabel  : string
      ToLabel    : string
      AsJson     : bool
      EmitSql    : bool
      EmitImpact : bool
      Review     : bool
      Planned    : PlanAction }

/// `check estate`'s coordinates (the `CheckEstate` payload). `TargetLabel` is
/// the masthead's display name for the unification basis; `Target` is its
/// resolution source; `Confirm` the (label, D9 conn-ref) environments the
/// estate verdict needs — every one of them (no partial estate; an unreadable
/// environment refuses by name at the face).
and CheckEstateArgs =
    { TargetLabel : string
      Target      : EstateTargetSource
      Confirm     : (string * string) list
      /// The `model` section whose `modules` selection scopes every OSSYS read
      /// (target + each confirm env) to the declared cutover module surface —
      /// excluding clone / deleted / test / system eSpaces, whose cloned
      /// entities would otherwise duplicate the cutover module's SS_Keys and
      /// fail the estate read (`catalog.kinds.duplicateKey`). The reads route
      /// through `ScopedRead` (pushdown + `ModuleFilter` backstop). An empty
      /// `model.modules` is the show-everything identity (byte-identical
      /// default).
      Scope       : Config.ModelSection
      AsJson      : bool
      Evidence    : EstateEvidenceMode
      /// `readiness.estate.repairBand` (wave A6) — `None` rides the
      /// engine's named default.
      RepairBand  : int64 option
      /// `readiness.estate.repairBandByEntity` — per-entity band overrides
      /// (logical entity name → band). Empty = the default governs all.
      RepairBandByEntity : Map<string, int64>
      /// `readiness.estate.decisionFloor` / `readiness.estate.asymmetryFactor`
      /// (DECISIONS 2026-07-18) — the A44 tuning knobs; `None` rides the
      /// engine's named defaults.
      DecisionFloor : int64 option
      AsymmetryFactor : int64 option
      /// `readiness.estate.promotionOrder` (DECISIONS 2026-07-18) — the
      /// declared promotion lattice, most-upstream first. Empty ⇒ the
      /// deployed↔deployed regime is silent (no order assumed).
      PromotionOrder : string list
      /// `--since @runId` (wave A7): the burndown's NAMED baseline — a
      /// recorded reading in the evidence store's history (never the run
      /// ledger). `None` = the latest recorded reading.
      Since       : string option
      /// `readiness.estate.fidelityFlow` (wave A4β/RT-10): the flow whose
      /// row-fidelity proof the board folds into its verdict. `Some flow` ⇒
      /// the face reads `fidelity.rows.json`, mints `ProofMissing`/
      /// `ProofStale`/`ProofDiverged` as needed, and the masthead states the
      /// clause; `None` ⇒ the clause is not configured and the verdict
      /// excludes it (RT-10).
      FidelityFlow : string option
      /// The loaded config's tightening section (wave A6): the face binds
      /// it against the resolved target catalog to read the ACTIVE interim
      /// posture — the relaxation keys whose meter lines the board carries.
      Tightening  : Config.TighteningSection option
      /// `overrides.tableRenames` (logical-key or physical form): the emission
      /// audit groups duplicate table names by the EMITTED name so an authored
      /// rename clears the collision on the board. A bad rename is a NAMED
      /// exit-2 config refusal at the face (mirrors the tightening bind).
      TableRenames : Config.TableRename list }

/// The row-fidelity proof's operands (T17, wave B2): the two environments
/// by label + resolved conn, the model reference whose rename map closes the
/// physical-to-logical gap, the optional kind/module scope, and the cap on
/// NAMED differences (the totals stay exact).
and CheckDataRowsArgs =
    { BeforeLabel : string
      BeforeConn  : string
      AfterLabel  : string
      AfterConn   : string
      ModelRef    : string
      Kind        : string option
      Module      : string option
      SampleCap   : int
      AsJson      : bool
      /// The intervention ledger's path (`--interventions <journal>`, wave
      /// B4b) — a transfer journal file, or the `--journal` directory that
      /// holds exactly one; `@runId` resolves through the run store's
      /// recorded `JournalRef` (wave B4a). `None` claims strict byte-identity.
      Interventions : string option
      /// The approved data corrections (`emission.dataCorrections`) replayed onto
      /// the SOURCE before comparing — a corrected target proves byte-identical
      /// against the replayed source. Empty ⇒ raw byte-identity.
      Corrections : ApprovedDataCorrection list }

/// How `check fidelity <flow>` stands the target's shape up on the container
/// stand-in before the load + proof (P1-S1): apply the emitted DDL batch (the
/// executor's path — today's default), or publish the emitted `.dacpac`
/// through DacFx (`DacServices.Deploy` — what a declarative deploy realizes).
/// The load and the proof are IDENTICAL across both modes: a model-built
/// dacpac is schema-only by construction, so the data still arrives through the
/// transfer machinery. "Byte-identical" is asserted at the deployed-schema +
/// row grain, never on dacpac bytes (which embed a wall-clock — `BACKLOG.md`
/// Slice ζ names that deferral).
and [<RequireQualifiedAccess>] StagingMode =
    /// Apply `SsdtDdlEmitter.statements` as a batch through `Deploy.executeBatch`.
    | Ddl
    /// Publish `DacpacEmitter.emit` through `Deploy.deployDacpac`.
    | Dacfx

/// P1-S4 — HOW the stand-in's ROWS are loaded before the proof (a second
/// staging axis beside `StagingMode`, which stages the SCHEMA). `Transfer`
/// (the default) runs the journaled wipe-and-load transfer machinery — the
/// tool's own extraction leg. `Lanes` instead applies the EMITTED data-lane
/// artifacts (the live-hydrated StaticSeeds + Bootstrap MERGE lanes, composed
/// against the logical rendition and applied through `Deploy.executeLeveledSeed`)
/// — the operator's OWN hand-apply path ("prove what I ship"): the lanes bracket
/// IDENTITY_INSERT, so the source keys land directly, no transfer and no journal.
and [<RequireQualifiedAccess>] LoadMode =
    /// The journaled transfer (the pre-P1-S4 behaviour, byte-identical).
    | Transfer
    /// The emitted StaticSeeds + Bootstrap data lanes, hydrated and applied.
    | Lanes

/// The container proof's operands (`check fidelity <flow>`, wave B5): the
/// flow, its live source (the estate being proven), the named-difference cap,
/// and the output form. The model arrives separately on the `PlanAction`
/// (the `needCatalog` seam).
and CheckFidelityFlowArgs =
    { Flow       : string
      /// The flow's `from` environment — the proof's source of truth.
      FromLabel  : string
      SourceConn : string
      /// `--sample N` — the cap on NAMED differences per kind (the totals
      /// stay exact); the `check data --rows` default.
      SampleCap  : int
      AsJson     : bool
      /// `--refresh` — force a full re-prove, ignoring and clearing this flow's
      /// cached proof (wave B6). The cache otherwise skips the expensive
      /// container proof when the model + source fingerprints are unchanged.
      Refresh    : bool
      /// `--stage ddl|dacfx` (P1-S1) — how the stand-in's SCHEMA is staged
      /// before the load. `Ddl` (the default) keeps the pre-P1-S1 behaviour
      /// byte-identically; `Dacfx` proves the extraction through a DacFx-published
      /// target. A `Dacfx` run always runs the container proof (it never reads or
      /// writes the DDL-keyed incremental cache — the DacFx≡DDL equivalence is the
      /// very thing under proof, so it is not assumed for cache reuse).
      Stage      : StagingMode
      /// `--capture <path>` (P2-S2) — write a PORTABLE proof manifest (the
      /// source's per-kind RowDigestFold digests + capture provenance) to
      /// `<path>`, for a later OFFLINE reconcile (`check fidelity --against`,
      /// P2-S3) against a target the tool did not stage. Forces a full proof run
      /// (the manifest needs the report's per-kind source digests, which a cache
      /// hit does not carry). `None` writes no manifest.
      Capture    : string option
      /// P1-S3 — the identity disposition the proof's load runs under, derived at
      /// parse time from the flow's TARGET (`flow.To`) archetype (the production
      /// sink's policy): `PreferPreservedKeys` for a FullRights target
      /// (IDENTITY_INSERT — source keys written directly, no capture/remap, no
      /// journal), else `Structural` (the sink mints IDENTITY keys and the
      /// ledger-modulated replay reconciles — the pre-P1-S3 default, byte-identical
      /// for an undeclared / ManagedDml target). So the container proof reproduces
      /// the identity handling the real cutover load would perform.
      IdentityPolicy : IdentityPolicy
      /// `--data transfer|lanes` (P1-S4) — how the stand-in's ROWS are loaded.
      /// `Transfer` (the default) uses the journaled transfer machinery (the
      /// pre-P1-S4 behaviour); `Lanes` applies the emitted StaticSeeds+Bootstrap
      /// data lanes (the operator's hand-apply path), proving that what the tool
      /// SHIPS reproduces the source byte-identical. A `Lanes` load writes the
      /// source keys directly (the lanes bracket IDENTITY_INSERT) and needs no
      /// transfer journal, so the compare aligns by identity.
      Load       : LoadMode
      /// The approved data corrections (`emission.dataCorrections`) replayed onto
      /// the SOURCE before comparing — a corrected target proves byte-identical
      /// against the replayed source. Empty ⇒ raw byte-identity.
      Corrections : ApprovedDataCorrection list
      /// `--correction-receipts <path>` — the RECORDED receipts a prior publish/
      /// load episode produced (a JSON array, or a run's `fidelity.rows.json`).
      /// When present, the proof reconciles the replayed receipts against these
      /// recorded counts, so a drifted/tampered receipt reds the proof by name.
      /// `None` ⇒ the replay just greens the proof (no reconciliation).
      CorrectionReceipts : string option }

/// The OFFLINE reconcile's operands (`check fidelity --against <manifest>
/// --target <ref>`, P2-S3): the portable manifest path, and the target the
/// operator applied themselves (label + resolved conn). No source — the manifest
/// IS the source's captured fingerprint. The model arrives on the `PlanAction`
/// (the `needCatalog` seam), its shape gated against the manifest's basis.
and CheckFidelityAgainstArgs =
    { ManifestPath : string
      /// The target env's display label (`--target`'s raw ref).
      TargetLabel  : string
      TargetConn   : string
      AsJson       : bool }

/// How `check estate` acquires each environment's data evidence
/// (DECISIONS 2026-07-15, the estate chapter opens, entry 4).
and [<RequireQualifiedAccess>] EstateEvidenceMode =
    /// The default: stored evidence rides when its fingerprints hold; a
    /// moved kind re-profiles its environment (pay once, stay honest).
    | FingerprintGated
    /// `--refresh [env,…]` — force re-profiling: every environment, or the
    /// named subset.
    | Refresh of envs: string list option
    /// `--offline` — reuse stored evidence unprobed; every verdict standing
    /// on it downgrades to advisory (named, never silent).
    | Offline

/// The estate target's resolution source: the agreed environment's live OSSYS
/// conn-ref (the `readiness.schema` default), or the authored model under the
/// live-OSSYS-primary / file-fallback policy (`ModelResolution`).
and [<RequireQualifiedAccess>] EstateTargetSource =
    | AgreedEnv of connRef: string
    | AuthoredModel of modelOssys: string option * modelFile: string option

/// A planned execution: the unhonored-axis notes (surfaced, never dropped —
/// fidelity #2) plus the routed action.
type ExecutionPlan =
    {
        Notes  : string list
        Action : PlanAction
    }
