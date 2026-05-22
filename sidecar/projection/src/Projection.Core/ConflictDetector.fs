namespace Projection.Core

/// A structural conflict detected in a projection run's lineage and
/// diagnostics (H-034).
///
/// Conflicts surface situations where operator-supplied policy choices
/// produce logical inconsistencies — an `OperatorIntent` transform fires on
/// a kind that the Selection policy excludes, or a non-Selection axis
/// emits a diagnostic for a kind that Selection has already removed.
///
/// These are not necessarily errors (some may be operator-intentional);
/// they are *warnings* surfaced for review.
type PolicyConflict =
    /// An operator-intent transform targeted a kind that the Selection
    /// policy removed. The transform's work was wasted — no output for
    /// that kind appears in the final artifacts.
    | UnreachableTransform of passName: string * ssKey: SsKey
    /// A non-Selection axis emitted a diagnostic naming an SsKey that
    /// the Selection axis had already removed. The axis acted on a
    /// kind the catalog never surfaces.
    | AxisContradiction of axis: OverlayAxis * ssKey: SsKey * code: string * message: string


/// Conflict detection from a projection run's lineage trail and diagnostics
/// (H-034).
///
/// **Discipline.** Both detectors gate on Selection-removal evidence
/// from the lineage trail — they do NOT flag normal evidence-based
/// outcomes (e.g., `tightening.nullability.relaxedUnderEvidence` on a
/// visible kind is a working pass, not a conflict). The HORIZON spec is
/// the canonical statement: "An axis contradiction is flagged when a
/// diagnostic code starts with a known policy-axis prefix AND the
/// corresponding policy axis had no visible effect."
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

    /// Map a diagnostic code's top-prefix to its `OverlayAxis`. Returns
    /// `None` for codes that do not name a policy axis (e.g.
    /// `profiling.*`, `topology.*` — these route to non-policy concerns).
    let private axisOfCode (code: string) : OverlayAxis option =
        if   code.StartsWith "selection."   then Some Selection
        elif code.StartsWith "tightening."  then Some Tightening
        elif code.StartsWith "emission."    then Some Emission
        elif code.StartsWith "insertion."   then Some Insertion
        else None

    /// Detect `AxisContradiction` conflicts. The gate is two-pronged:
    ///   1. The diagnostic code names a policy axis (selection / tightening
    ///      / emission / insertion).
    ///   2. The diagnostic carries an `SsKey` that the Selection axis has
    ///      already removed — i.e. the diagnostic acted on a kind the
    ///      catalog never surfaces, so the axis's effect was wasted.
    ///
    /// **Why both gates.** Without (2), every working `tightening.*` and
    /// `selection.*` diagnostic emitted by the production passes would be
    /// flagged. Normal evidence-based outcomes (e.g.
    /// `tightening.nullability.relaxedUnderEvidence` on a visible kind)
    /// are the **success path** of the corresponding pass, not a conflict.
    /// The gate matches HORIZON's "the corresponding policy axis had no
    /// visible effect" condition.
    let private axisContradictions
        (removedKeys: SsKey Set)
        (diagnostics: DiagnosticEntry list)
        : PolicyConflict list =
        diagnostics
        |> List.choose (fun d ->
            match d.SsKey, axisOfCode d.Code with
            | Some key, Some axis when Set.contains key removedKeys ->
                // Axis fired on a kind Selection removed → contradiction.
                // Selection-axis codes targeting a removed kind are not
                // self-contradictions (the removal itself produces the
                // code) — exclude that case.
                if axis = Selection then None
                else Some (AxisContradiction (axis, key, d.Code, d.Message))
            | _ -> None)

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
        let removedKeys      = removedBySelectionPolicy events
        let unreachable      = unreachableTransforms removedKeys events
        let contradictions   = axisContradictions removedKeys diagnostics
        unreachable @ contradictions
