namespace Projection.Core

/// Named downgrade diagnostics for the evidence-tiering policy
/// (`SamplingPolicy`) — a sampled kind's evidence downgrade is operator-
/// REQUESTED, but it is still a downgrade, and downgrades are never
/// silent: one entry per catalog kind whose cell evidence the policy
/// caps, naming exactly what stays exact and what loses exhaustiveness.
[<RequireQualifiedAccess>]
module SamplingDiagnostics =

    /// One `Info` entry per sampled kind (operator-requested ⇒ not a
    /// Warning; named ⇒ not silence). Full-scan policy emits nothing.
    let emit (catalog: Catalog) (policy: SamplingPolicy) : DiagnosticEntry list =
        if SamplingPolicy.isFullScan policy then []
        else
            Catalog.allKinds catalog
            |> List.choose (fun k ->
                match SamplingPolicy.capFor k.SsKey policy with
                | None -> None
                | Some cap ->
                    Some
                        (DiagnosticEntry.create
                            "profiler:sampling"
                            DiagnosticSeverity.Info
                            "profiler.evidence.sampled"
                            (sprintf
                                "%s: cell evidence sampled to %d rows by the profiler sampling policy. Row and null counts remain EXACT (the aggregate query is never capped); distribution, duplicate/uniqueness, and observed-length evidence downgrade to observed-sample confidence (ProbeStatus carries the sample size). This kind is excluded from single-scan derivation and keeps the live capped discovery."
                                (Name.value k.Name)
                                cap)))
