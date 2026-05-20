namespace Projection.Targets.OperationalDiagnostics

open Projection.Core

/// Chapter B.4 slice 6 — operationalize logging-format contract §12
/// (`suggestedConfig` actionable-payload discipline) on the JSON-
/// artifact emitters + severity-sort + axis-cluster for navigation.
///
/// **Scope shape (post-reshape; see `DECISIONS 2026-05-20 (slice
/// B.4.6 reshape — drop occluding cluster-cap)`).** Two-part work:
///
/// 1. **`suggestedConfig` payload.** Every actionable entry carries
///    a `SuggestedConfig` record (`Projection.Core.Diagnostics.fs`)
///    pointing at the JSON path + value the operator could apply to
///    fix the finding. The JSON-emit path writes `suggestedConfig:
///    { path, value, note }` per §12; entries without an actionable
///    config-edit omit the field (back-compat with pre-slice-6
///    consumers).
///
/// 2. **Severity-sort + axis-cluster for navigation.** Findings are
///    sorted by severity descending (Error > Warning > Info) within
///    each axis; entries grouped by their `Axis.tryFromCode`-derived
///    cluster key. Operators reading the JSON see same-axis findings
///    contiguous + the highest-severity within an axis first. **No
///    occlusion**: every input entry surfaces in the output; no
///    findings are dropped.
///
/// **What this slice does NOT do (deliberate scope limit per the
/// reshape).** No cluster-cap with overflow suppression. The earlier
/// design dropped entries beyond `MaxPerAxis = 10` and surfaced only
/// the suppressed count — but every dropped entry is a real source
/// defect (a NULL in a NOT NULL column, an orphaned FK row, a
/// duplicate unique-index candidate). Operators MUST see each one;
/// occluding them at the emit boundary loses signal. The right
/// response to "V1 diagnostics are noisy" is per-finding-type
/// emission gates at the source (strategy/pass-layer thresholds like
/// `NullabilityTighteningConfig.NullBudget`), not after-the-fact
/// suppression at the emit boundary. Chapter C slice C.1 (tightening
/// axis) wires operator-config to those existing source-layer knobs.
///
/// **Pillar 9 classification.** Pure `DataIntent` enrichment. Sort +
/// cluster derive structurally from `Code` + `Severity`; no operator
/// opinion is introduced. The `SuggestedConfig` payload derives from
/// the finding's evidence + V2's structural knowledge of which config
/// path would address it; the suggestion is a data-derived
/// observation, not an operator-supplied overlay.

/// Axis derivation primitives. Per V2's `Code` convention (dot-
/// separated with a routing top-prefix — `tightening.*`,
/// `profiling.*`, `adapter.*`, `emit.*`, etc.), the **axis** is the
/// two-segment prefix (top + sub-domain) used as the cluster key
/// for presentation ordering. Used at slice 6 (this file) for
/// JSON-artifact ordering; also a natural cluster key for slice
/// 6.5's `summary.runComplete` roll-up algorithm (per logging-
/// format contract §11 group key 3-tuple).
[<RequireQualifiedAccess>]
module Axis =

    /// Extract the clustering axis from a `Code`. Returns `Some axis`
    /// for dot-separated codes; `None` for blank codes. Two-segment
    /// axis preferred (`tightening.nullability`); single-segment
    /// falls back to the top.
    let tryFromCode (code: string) : string option =
        if System.String.IsNullOrWhiteSpace code then None
        else
            let segments = code.Split('.')
            match segments.Length with
            | 0 -> None
            | 1 ->
                if System.String.IsNullOrWhiteSpace segments.[0] then None
                else Some segments.[0]
            | _ ->
                let top = segments.[0]
                let sub = segments.[1]
                if System.String.IsNullOrWhiteSpace top then None
                elif System.String.IsNullOrWhiteSpace sub then Some top
                else Some (sprintf "%s.%s" top sub)


/// Severity-sort + axis-cluster pipeline for diagnostic-emit
/// navigation. Pure DataIntent; no occlusion.
[<RequireQualifiedAccess>]
module ActionableDiagnostics =

    /// Severity ordering: Error (rank 0; highest) > Warning (1) >
    /// Info (2). Lower rank = higher severity. Stable sort key.
    let private severityRank (s: DiagnosticSeverity) : int =
        match s with
        | DiagnosticSeverity.Error   -> 0
        | DiagnosticSeverity.Warning -> 1
        | DiagnosticSeverity.Info    -> 2

    /// Stable sort key. Primary: severity rank ascending (Error
    /// first). Secondary: SsKey root-original string ascending
    /// (lexicographic; stable across runs). Tertiary: Code ascending
    /// (stable within an axis when an SsKey has multiple entries).
    /// T1 byte-determinism holds because every dimension is
    /// structural.
    let private retentionKey (e: DiagnosticEntry) : int * string * string =
        let ssKeyStr =
            match e.SsKey with
            | Some k -> SsKey.rootOriginal k
            | None   -> ""
        severityRank e.Severity, ssKeyStr, e.Code

    /// Reorder diagnostic entries for operator-readable presentation.
    /// Pipeline:
    ///   1. Partition entries by `Axis.tryFromCode`. Entries without
    ///      a derivable axis pass through unclustered (tail of the
    ///      result) preserving input order.
    ///   2. Per axis: stable-sort by `retentionKey` (severity desc,
    ///      SsKey ASC, Code ASC). All entries retained.
    ///   3. Reassemble: clustered axes (sorted by axis name) ++
    ///      unclustered tail.
    ///
    /// **No occlusion.** Every input entry surfaces in the output.
    /// `(organize entries).Length = entries.Length` always.
    ///
    /// **Big-O.** O(N log N) over total entry count (single sort
    /// pass after the group-by). The group-by step is O(N); the
    /// per-axis sort step is O(sum(per-axis size × log per-axis
    /// size)) = O(N log N) amortized.
    let organize (entries: DiagnosticEntry list) : DiagnosticEntry list =
        use _ = Bench.scope "actionableDiagnostics.organize"
        let unclustered = ResizeArray<DiagnosticEntry>()
        let byAxis = System.Collections.Generic.Dictionary<string, ResizeArray<DiagnosticEntry>>()
        for e in entries do
            match Axis.tryFromCode e.Code with
            | None -> unclustered.Add e
            | Some axis ->
                match byAxis.TryGetValue axis with
                | true, lst -> lst.Add e
                | false, _ ->
                    let lst = ResizeArray<DiagnosticEntry>()
                    lst.Add e
                    byAxis.[axis] <- lst
        let result = ResizeArray<DiagnosticEntry>()
        let sortedAxes =
            byAxis
            |> Seq.map (fun kvp -> kvp.Key)
            |> Seq.sort
            |> Seq.toList
        for axis in sortedAxes do
            let group = byAxis.[axis]
            let sorted = group |> Seq.sortBy retentionKey
            for e in sorted do result.Add e
        for e in unclustered do result.Add e
        List.ofSeq result
