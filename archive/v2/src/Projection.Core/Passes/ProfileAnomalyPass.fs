namespace Projection.Core.Passes

// LINT-ALLOW-FILE: pass-driver lineage diagnostic prose. The anomaly-detection pass emits
//   operator-facing messages (null-rate / CV vs mean+2σ) via `sprintf` and uses
//   function-local mutables for the statistical accumulation. Structural output
//   is typed; only the diagnostic prose surface uses sprintf, per
//   `DECISIONS 2026-05-09 — Built-in obligation`.

open Projection.Core

/// H-073 — Anomaly detection in Profile. Flags columns whose null
/// rate or coefficient of variation falls more than 2σ above the
/// per-table mean. Consumes `Profile.ColumnProfiles` for null-rate
/// analysis and `NumericDistribution.coefficientOfVariation` for CV
/// analysis.
///
/// Inputs: `Catalog` (to group attributes by kind) + `Profile` (for
/// empirical statistics). Output: `ProfileAnomalyReport`.
[<RequireQualifiedAccess>]
module ProfileAnomalyPass =

    [<Literal>]
    let version : int = 1

    [<Literal>]
    let private passName : string = "profileAnomaly"

    // Anomaly threshold: N standard deviations above the mean — advisory tuning
    // (a default operator opinion, overridable), single-sourced from
    // `AdvisoryTuning.defaults` (the default is 2σ). NOT [<Literal>] on decimal
    // per cctor-bomb discipline.
    let private sigmaThreshold : decimal = AdvisoryTuning.defaults.ProfileAnomalySigma

    /// Compute mean and population standard deviation of a decimal list.
    /// Returns (0m, 0m) for empty or single-element lists (σ undefined).
    let private meanAndStdDev (values: decimal list) : decimal * decimal =
        match values with
        | [] | [_] -> 0.0m, 0.0m
        | _ ->
            let n = decimal (List.length values)
            let mean = List.sum values / n
            let variance =
                values
                |> List.sumBy (fun v -> (v - mean) * (v - mean))
                |> fun s -> s / n
            // Integer-safe sqrt approximation via Newton's method on decimal.
            let sqrtDecimal (x: decimal) : decimal =
                if x <= 0.0m then 0.0m
                else
                    // recon #19 — the same fixed-point scheme as the convergence
                    // loops, here with no convergence test (a pure 20-iteration
                    // cap): the step never reports convergence.
                    x / 2.0m
                    |> Fixpoint.iterate 20 (fun guess -> (guess + x / guess) / 2.0m, false)
                    |> fst
            mean, sqrtDecimal variance

    let run (catalog: Catalog) (profile: Profile) : Lineage<Diagnostics<ProfileAnomalyReport>> =
        use _ = Bench.scope "pass.profileAnomaly"

        let allKinds = Catalog.allKinds catalog

        // Null-rate anomaly: per kind, compute null rate per attribute,
        // then flag attributes > mean + 2σ.
        let highNullRate =
            allKinds
            |> List.collect (fun k ->
                let nullRates =
                    k.Attributes
                    |> List.choose (fun a ->
                        match Profile.tryFindColumn a.SsKey profile with
                        | None -> None
                        | Some cp ->
                            if cp.RowCount > 0L then
                                Some (a.SsKey, decimal cp.NullCount / decimal cp.RowCount)
                            else None)
                if List.length nullRates < 2 then []
                else
                    let rates = nullRates |> List.map snd
                    let mean, sigma = meanAndStdDev rates
                    let threshold = mean + sigmaThreshold * sigma
                    nullRates
                    |> List.filter (fun (_, rate) -> rate > threshold))
            |> List.sortBy fst

        // CV anomaly: per kind, collect CV from numeric distributions,
        // then flag attributes > mean CV + 2σ.
        let highCv =
            allKinds
            |> List.collect (fun k ->
                let cvs =
                    k.Attributes
                    |> List.choose (fun a ->
                        match Profile.tryFindNumeric a.SsKey profile with
                        | None -> None
                        | Some dist ->
                            NumericDistribution.coefficientOfVariation dist
                            |> Option.map (fun cv -> a.SsKey, cv))
                if List.length cvs < 2 then []
                else
                    let cvValues = cvs |> List.map snd
                    let mean, sigma = meanAndStdDev cvValues
                    let threshold = mean + sigmaThreshold * sigma
                    cvs
                    |> List.filter (fun (_, cv) -> cv > threshold))
            |> List.sortBy fst

        let report =
            { HighNullRateColumns = highNullRate
              HighCvColumns       = highCv }

        let nullDiagnostics =
            highNullRate
            |> List.map (fun (key, rate) ->
                { DiagnosticEntry.create passName DiagnosticSeverity.Warning
                    "profiling.anomaly.nullRate.high"
                    (sprintf "Attribute %s null rate %.4f exceeds table mean + 2σ"
                        (SsKey.rootOriginal key) rate)
                  with SsKey = Some key })

        let cvDiagnostics =
            highCv
            |> List.map (fun (key, cv) ->
                { DiagnosticEntry.create passName DiagnosticSeverity.Warning
                    "profiling.anomaly.cv.high"
                    (sprintf "Attribute %s CV %.4f exceeds table mean CV + 2σ"
                        (SsKey.rootOriginal key) cv)
                  with SsKey = Some key })

        let allDiagnostics = nullDiagnostics @ cvDiagnostics

        let touched = allKinds |> List.map (fun k -> k.SsKey)
        LineageDiagnostics.touchedEpilogue passName version touched allDiagnostics report

    let registered (profile: Profile) : RegisteredTransform<Catalog, ProfileAnomalyReport> =
        { Name         = passName
          Domain       = CrossCutting
          StageBinding = Pass
          Sites =
            [ { SiteName       = "nullRateAnomaly"
                Classification = DataIntent
                Rationale      = "Per-column null rate vs table-mean + Nσ threshold. Advisory only — the null rates are Profile.ColumnProfiles empirical evidence, but the σ multiplier is a DEFAULT OPERATOR OPINION (overridable, `AdvisoryTuning.defaults.ProfileAnomalySigma`, default 2σ); the flag never enters the faithful projection." }
              { SiteName       = "cvAnomaly"
                Classification = DataIntent
                Rationale      = "Per-column coefficient of variation vs table-mean CV + Nσ threshold. Advisory only — the CVs are NumericDistribution.Moments empirical evidence, but the σ multiplier is a DEFAULT OPERATOR OPINION (overridable, `AdvisoryTuning.defaults.ProfileAnomalySigma`, default 2σ); the flag never enters the faithful projection." } ]
          Run    = fun c -> run c profile
          Status = Active }
