namespace Projection.Pipeline

// LINT-ALLOW-FILE: diagnostic message text built via `sprintf` with
// numeric interpolation (distinct count, mean rows-per-value). No
// typed BCL alternative produces equivalent diagnostic prose; allowed
// exception per LINT-ALLOW substantive-rationale discipline.

open Projection.Core

/// H-025 — ForeignKeySelectivity consumers. Reads
/// `Profile.ForeignKeySelectivities` and emits one `DiagnosticEntry`
/// (Info) per FK reference whose selectivity evidence suggests a
/// covering index would improve JOIN and lookup performance.
///
/// **Selectivity metric.** `meanMatchCount = totalObservedRows /
/// DistinctCount`. When this is below the threshold (< 2.0), each FK
/// value appears in very few rows on average — a B-tree index on the
/// FK source column would return very few rows per lookup and
/// dramatically reduce JOIN cost.
///
/// **Guard condition.** `DistinctCount >= 10` prevents noise on
/// trivially small or sparse FK probes.
///
/// **Pillar 9 classification.** Pure `DataIntent` scan — derived
/// entirely from profile evidence; no operator opinion is introduced.
/// The operator acts *after* seeing the diagnostic.
///
/// **Empty-profile identity.** Returns `[]` when
/// `Profile.ForeignKeySelectivities` is empty.
[<RequireQualifiedAccess>]
module FkSelectivityDiagnostics =

    [<Literal>]
    let private source = "emitter:indexAdvisor"

    [<Literal>]
    let private code = "profiling.fkSelectivity.highSelectivityCandidate"

    // Threshold: if average rows per FK value < 2.0, the FK column
    // is highly selective — B-tree lookup returns very few rows.
    // `[<Literal>]` is not used on `decimal` — F# / .NET 9 emit a
    // `DecimalConstantAttribute` that the CLR rejects at cctor time
    // (`InvalidProgramException`); the discipline is "Literal only for
    // CLR-primitive constants" and `decimal` is a struct, not a primitive.
    let private meanMatchThreshold : decimal = 2.0m

    // Guard: suppress noise on tables with fewer than 10 distinct FK values.
    [<Literal>]
    let private minDistinctCount = 10L

    /// Emit one `DiagnosticEntry` per high-selectivity FK reference.
    /// Returns `[]` when no reference meets the selectivity threshold.
    let emit (profile: Profile) : DiagnosticEntry list =
        profile.ForeignKeySelectivities
        |> List.choose (fun sel ->
            if sel.DistinctCount < minDistinctCount then None
            else
                let totalObserved = sel.Frequencies |> List.sumBy snd
                if totalObserved = 0L then None
                else
                    let meanMatchCount =
                        decimal totalObserved / decimal sel.DistinctCount
                    if meanMatchCount >= meanMatchThreshold then None
                    else
                        Some
                            { DiagnosticEntry.create source DiagnosticSeverity.Info code
                                (sprintf
                                    "FK column has high selectivity (%d distinct values, %.4G average rows per value). A covering index on the FK source column would improve JOIN and lookup performance."
                                    sel.DistinctCount
                                    meanMatchCount)
                              with
                                SsKey    = Some sel.ReferenceKey
                                Metadata =
                                    Map.ofList
                                        [ "distinctCount",
                                          sel.DistinctCount.ToString(
                                              System.Globalization.CultureInfo.InvariantCulture)
                                          "meanMatchCount",
                                          meanMatchCount.ToString(
                                              "G4",
                                              System.Globalization.CultureInfo.InvariantCulture)
                                          "isTruncated",
                                          string sel.IsTruncated ] })
