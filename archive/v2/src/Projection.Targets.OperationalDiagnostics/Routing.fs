namespace Projection.Targets.OperationalDiagnostics

open Projection.Core

/// Code-prefix routing table for chapter 4.3's three operator-
/// facing diagnostic artifacts (per pre-scope §1.3 + §1.4
/// "refuse the split; artifacts ARE the channels"). The three
/// sibling emitters consume this primitive — extraction earned
/// its place at the second consumer (`OpportunitiesEmitter` at
/// slice β; `ValidationsEmitter` at slice γ; `DecisionLogEmitter`
/// at slice α was the first).
///
/// Routing convention (per pre-scope §1.3):
/// ```
/// tightening.*.opportunity.*       → Opportunities
/// tightening.*.validation.*        → Validations
/// tightening.*  (everything else)  → DecisionLog
/// adapter.*    (boundary errors)   → DecisionLog
/// emitter.*    (Π-time errors)     → DecisionLog
/// userFkReflow.*                   → DecisionLog
/// <any other prefix>               → DecisionLog
/// ```
///
/// The partition property (slice γ): every `DiagnosticEntry`
/// routes to exactly one artifact (no entry orphaned; no entry
/// double-counted). Property test in
/// `OperationalDiagnosticsRoutingTests.fs`.

/// The three operator-facing artifact channels. Concept-shaped
/// per pillar 8: each variant names the artifact's operator-
/// vocabulary purpose (audit / actionable / pass-witnessed).
type DiagnosticArtifact =
    /// Audit channel — every decision the system made.
    /// Default for entries that don't match the opportunity /
    /// validation prefixes.
    | DecisionLog
    /// Operator channel — actionable suggestions.
    /// Matches Code prefix `tightening.*.opportunity.*`.
    | Opportunities
    /// Developer channel — pass-witnessed invariant confirmations.
    /// Matches Code prefix `tightening.*.validation.*`.
    | Validations


[<RequireQualifiedAccess>]
module DiagnosticArtifact =

    /// Render the artifact as the V1-conventional filename. Used
    /// by the CLI wire-up (slice δ — deferred) to name the output
    /// file; per pre-scope §1.1.
    let filename (artifact: DiagnosticArtifact) : string =
        match artifact with
        | DecisionLog    -> "decision-log.json"
        | Opportunities  -> "opportunities.json"
        | Validations    -> "validations.json"


[<RequireQualifiedAccess>]
module Routing =

    /// Route one `DiagnosticEntry` to its artifact channel based
    /// on its `Code` field. Pure function — no I/O, no allocation
    /// beyond pattern-match. Tested via the partition property:
    /// every entry routes to exactly one of the three artifacts.
    ///
    /// The Code-prefix table is the single point of routing
    /// decision; per pre-scope §1.3 this "dissolves the 'which
    /// channel does this entry belong to?' question." Future
    /// `Code` namespaces extend the match without restructuring
    /// the writer.
    let route (entry: DiagnosticEntry) : DiagnosticArtifact =
        // F# 9 nullness-strict declares `DiagnosticEntry.Code :
        // string` non-nullable, but FsCheck-generated property-test
        // inputs and boundary adapters can supply a null reference
        // at runtime regardless. `System.String.IsNullOrEmpty` is
        // the BCL primitive that handles both null and empty
        // strings defensively; routing totalizes over the full
        // input domain (per total-decisions discipline).
        let code = entry.Code
        if System.String.IsNullOrEmpty code then DecisionLog
        elif code.Contains ".opportunity." then Opportunities
        elif code.Contains ".validation." then Validations
        else DecisionLog

    /// Partition a list of entries by routing artifact. Returns
    /// a triple `(decisionLog, opportunities, validations)` —
    /// the per-artifact entry lists each emitter consumes.
    /// Preserves chronological order within each artifact.
    let partition
        (entries: DiagnosticEntry list)
        : DiagnosticEntry list * DiagnosticEntry list * DiagnosticEntry list =
        let folder
            (decisionLog: DiagnosticEntry list, opportunities: DiagnosticEntry list, validations: DiagnosticEntry list)
            (entry: DiagnosticEntry)
            : DiagnosticEntry list * DiagnosticEntry list * DiagnosticEntry list =
            match route entry with
            | DecisionLog    -> (entry :: decisionLog, opportunities, validations)
            | Opportunities  -> (decisionLog, entry :: opportunities, validations)
            | Validations    -> (decisionLog, opportunities, entry :: validations)
        let (rDecisionLog, rOpportunities, rValidations) =
            entries |> List.fold folder ([], [], [])
        // List.fold accumulates in reverse; reverse each bucket
        // to preserve chronological order.
        (List.rev rDecisionLog, List.rev rOpportunities, List.rev rValidations)
