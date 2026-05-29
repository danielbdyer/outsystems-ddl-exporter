namespace Projection.Pipeline

// LINT-ALLOW-FILE: diagnostic message text built via `sprintf` with
// numeric interpolation (uniqueness ratio, observed rows, attribute
// names). Allowed exception per LINT-ALLOW substantive-rationale
// discipline.

open Projection.Core

/// H-026 — JointDistribution consumers. Reads
/// `Profile.JointDistributions` and emits one `DiagnosticEntry`
/// (Info) per kind whose joint FK-column distribution shows
/// near-unique tuple combinations — a signal that a composite unique
/// constraint on those FK columns may be valid.
///
/// **Co-occurrence metric.** `uniquenessRatio = DistinctCount /
/// totalObservedRows`. When this exceeds the threshold (≥ 0.95),
/// nearly every row has a unique combination of FK values — the FK
/// columns are near-functionally independent and a composite unique
/// constraint would not be violated by the observed data.
///
/// **Guard condition.** `DistinctCount >= 5` prevents noise on
/// trivially small probe results.
///
/// **Pillar 9 classification.** Pure `DataIntent` scan — derived
/// entirely from profile evidence; no operator opinion is introduced.
///
/// **Empty-profile identity.** Returns `[]` when
/// `Profile.JointDistributions` is empty.
[<RequireQualifiedAccess>]
module JointDependencyDiagnostics =

    [<Literal>]
    let private source = "emitter:functionalDependency"

    [<Literal>]
    let private code = "profiling.jointDistribution.nearUniqueComposite"

    // Threshold: if uniquenessRatio >= 0.95, the tuple combination is
    // nearly unique across observed rows → composite unique constraint
    // may be valid.
    // `[<Literal>]` is not used on `decimal` — F# / .NET 9 emit a
    // `DecimalConstantAttribute` that the CLR rejects at cctor time
    // (`InvalidProgramException`); the discipline is "Literal only for
    // CLR-primitive constants" and `decimal` is a struct, not a primitive.
    let private uniquenessThreshold : decimal = 0.95m

    // Guard: suppress noise on very small probe results.
    [<Literal>]
    let private minDistinctCount = 5L

    /// Emit one `DiagnosticEntry` per kind where joint FK-column
    /// distribution shows near-unique combinations. Returns `[]`
    /// when no distribution meets the uniqueness threshold.
    let emit (profile: Profile) : DiagnosticEntry list =
        profile.JointDistributions
        |> List.choose (fun jd ->
            if jd.DistinctCount < minDistinctCount then None
            else
                let totalObserved = jd.Frequencies |> List.sumBy snd
                if totalObserved = 0L then None
                else
                    let uniquenessRatio =
                        decimal jd.DistinctCount / decimal totalObserved
                    if uniquenessRatio < uniquenessThreshold then None
                    else
                        let attrRoots =
                            jd.AttributeKeys
                            |> List.map SsKey.rootOriginal
                            |> String.concat ", "
                        Some
                            { DiagnosticEntry.create source DiagnosticSeverity.Info code
                                (sprintf
                                    "FK columns (%s) exhibit near-unique co-occurrence (%.4G uniqueness ratio across %d observed rows). A composite unique constraint on these columns may be valid."
                                    attrRoots
                                    uniquenessRatio
                                    totalObserved)
                              with
                                SsKey    = Some jd.KindKey
                                Metadata =
                                    Map.ofList
                                        [ "distinctCount",
                                          jd.DistinctCount.ToString(
                                              System.Globalization.CultureInfo.InvariantCulture)
                                          "uniquenessRatio",
                                          uniquenessRatio.ToString(
                                              "G4",
                                              System.Globalization.CultureInfo.InvariantCulture)
                                          "attributeCount",
                                          string (List.length jd.AttributeKeys)
                                          "isTruncated",
                                          string jd.IsTruncated ] })
