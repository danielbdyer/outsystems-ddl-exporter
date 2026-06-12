namespace Projection.Pipeline

open Projection.Core

/// R2 — the stage spine (`CONSTELLATION.md` §9.3; CONSTELLATION_BACKLOG
/// stage 2, card S1). Stage identity today is a string-prefix convention
/// over event codes (`<stage>.started` / `summary.stageCompleted{stage}`,
/// parsed by the Watch board) plus per-face display lists. The spine types
/// promote that convention to the type plane: a `StageName` is constructed
/// once, validly, or not at all; a `RunSpine` is the declared stage arc of
/// one run face — what the Watch pre-seeds from, and what the `staged { }`
/// CE (card S2) holds the run accountable to (`declared ⇔
/// executed∪aborted`).
///
/// The smart ctor is the contract (the house derive-macro): `private` case
/// + `[<RequireQualifiedAccess>]` companion makes an invalid stage name
/// unrepresentable downstream.
type StageName = private StageName of string

[<RequireQualifiedAccess>]
module StageName =

    /// Non-blank, dot-free. Stage names key the wire codes
    /// (`<stage>.started`; the `stage` payload of `summary.stageCompleted`
    /// / `summary.stageProgress`) where `.` is the code-namespace
    /// separator — a dotted stage name would collide with the prefix
    /// convention the Watch board parses (`Watch.apply`).
    let create (name: string) : Result<StageName> =
        let blankErrors =
            Validation.nonBlank "stage.name.empty" "Stage name must be provided." name
        let dottedErrors =
            if not (System.String.IsNullOrWhiteSpace name) && name.Contains "." then
                [ ValidationError.create
                    "stage.name.dotted"
                    "Stage name must not contain '.' — the envelope-code namespace separator." ]
            else []
        match blankErrors @ dottedErrors with
        | [] -> Result.success (StageName name)
        | es -> Result.failure es

    /// The wire key — the `<stage>` of `<stage>.started` and the `stage`
    /// payload value of the summary events.
    let value (StageName n) : string = n

/// The declared stage arc of one run face — distinct, non-empty, in
/// execution order. The Watch board pre-seeds `Pending` lines from it so
/// the whole arc is visible from the first frame; the `staged { }` CE
/// asserts `declared ⇔ executed∪aborted` at run end (an open stage at run
/// end becomes a named `Aborted`, never a board hang).
type RunSpine = private { Declared : StageName list }

[<RequireQualifiedAccess>]
module RunSpine =

    let create (stages: StageName list) : Result<RunSpine> =
        let emptyErrors =
            Validation.nonEmpty
                "spine.stages.empty"
                "A run spine must declare at least one stage."
                stages
        let duplicateErrors =
            stages
            |> Validation.duplicateKeyErrors
                "spine.stages.duplicateKey"
                (sprintf "Run spine declares stage '%s' more than once; declared stages are distinct.")
                StageName.value
        match emptyErrors @ duplicateErrors with
        | [] -> Result.success { Declared = stages }
        | es -> Result.failure es

    /// The declared stages, in execution order.
    let declared (spine: RunSpine) : StageName list = spine.Declared

/// The spine-level outcome of one declared stage. RI-2's correction is the
/// third arm: **aborted-at-stage is a real outcome** — a stage opened and
/// never closed because the run died inside it (e.g. `MigrationRun.execute`
/// opens "emit" and errors out before the close). The law admits it by
/// name rather than letting the board hang on an Active line. `Skipped`
/// is the declared-but-legitimately-not-run arm (a named reason, e.g. a
/// store leg absent from this invocation) — distinct from `Aborted`, which
/// is always a failure of the run to reach the stage's close.
type StagedOutcome =
    | Completed of durationMs: int64
    | Aborted of refusal: string
    | Skipped of reason: string
