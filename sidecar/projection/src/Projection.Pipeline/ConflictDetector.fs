namespace Projection.Pipeline

open Projection.Core

/// A structural conflict detected in a projection run's lineage and
/// diagnostics (H-034).
///
/// Conflicts surface situations where operator-supplied policy choices
/// produce logical inconsistencies — an `OperatorIntent` transform fires on
/// a kind that the Selection policy excludes, or a diagnostic code indicates
/// an axis violation. These are not necessarily errors (some may be
/// operator-intentional); they are *warnings* surfaced for review.
type PolicyConflict =
    /// An operator-intent transform targeted a kind that the Selection
    /// policy removed. The transform's work was wasted — no output for
    /// that kind appears in the final artifacts.
    | UnreachableTransform of passName: string * ssKey: SsKey
    /// Two diagnostics with conflicting axis codes were produced in the
    /// same run. For example, a `tightening.*` diagnostic fires alongside
    /// a `selection.*` exclusion for the same kind.
    | AxisContradiction of axis: OverlayAxis * code: string * message: string


/// Conflict detection from a projection run's lineage trail and diagnostics
/// (H-034).
[<RequireQualifiedAccess>]
module ConflictDetector =

    /// Extract the set of `SsKey`s removed by `OperatorIntent Selection`
    /// events (VisibilityMask, SelectionPolicy exclusions).
    let private removedBySelectionPolicy (events: LineageEvent list) : SsKey Set =
        events
        |> List.choose (fun e ->
            match e.TransformKind, e.Classification with
            | Removed _, OperatorIntent Selection -> Some e.SsKey
            | _                                  -> None)
        |> Set.ofList

    /// Detect `UnreachableTransform` conflicts: an OperatorIntent transform
    /// (other than Selection removal) targeted a kind already excluded by the
    /// Selection policy.
    let private unreachableTransforms
        (removedKeys: SsKey Set)
        (events: LineageEvent list)
        : PolicyConflict list =
        events
        |> List.choose (fun e ->
            match e.Classification with
            | OperatorIntent Selection -> None  // The removal itself — not a conflict
            | OperatorIntent _         ->
                if Set.contains e.SsKey removedKeys then
                    Some (UnreachableTransform (e.PassName, e.SsKey))
                else None
            | DataIntent -> None)

    /// Detect `AxisContradiction` conflicts from the diagnostics list.
    /// An axis contradiction is flagged when a diagnostic code starts with
    /// a known policy-axis prefix (`"selection."`, `"tightening."`, etc.)
    /// and the corresponding policy axis had no visible effect (the kind
    /// was excluded or the intervention produced no decision).
    let private axisContradictions (diagnostics: DiagnosticEntry list) : PolicyConflict list =
        diagnostics
        |> List.choose (fun d ->
            if d.Code.StartsWith "selection." then
                Some (AxisContradiction (Selection, d.Code, d.Message))
            elif d.Code.StartsWith "tightening." then
                Some (AxisContradiction (Tightening, d.Code, d.Message))
            elif d.Code.StartsWith "emission." then
                Some (AxisContradiction (Emission, d.Code, d.Message))
            elif d.Code.StartsWith "insertion." then
                Some (AxisContradiction (Insertion, d.Code, d.Message))
            else None)

    /// Detect all structural policy conflicts in a projection run.
    ///
    /// `events` — the full lineage trail from the projection.
    /// `diagnostics` — the `DiagnosticEntry list` from the same run.
    ///
    /// Returns a list of detected conflicts (possibly empty). Conflicts
    /// are not errors by themselves — they are observations for the
    /// operator to review. An empty return means no structural contradictions
    /// were detected.
    let detectConflicts
        (events: LineageEvent list)
        (diagnostics: DiagnosticEntry list)
        : PolicyConflict list =
        let removedKeys = removedBySelectionPolicy events
        let unreachable  = unreachableTransforms removedKeys events
        let contradictions = axisContradictions diagnostics
        unreachable @ contradictions
