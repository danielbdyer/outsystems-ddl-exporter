namespace Projection.Core.Passes

/// F6 (audit 2026-06-17) — the centralized ADVISORY-TUNING surface for the
/// `H-07x` analytics passes (SchemaComplexity / ProfileAnomaly / Centrality;
/// QueryHint names its own at the pass head). Every value here is a **default
/// operator opinion, overridable** — these drive advisory/diagnostic outputs
/// ONLY and never enter the faithful projection.
///
/// Lifting the per-pass private constants into one named, typed surface makes
/// the tuning discoverable in a single place (the "optional advisory-tuning
/// config" the sweep asked for) and single-sources the numbers. The passes
/// consume `AdvisoryTuning.defaults` today, so emission and every advisory
/// output are byte-identical to the prior per-pass literals.
///
/// A RUNTIME operator override — threading a non-default `T` through each pass's
/// `run`/`registered` — is the named follow-on, deferred deliberately: it would
/// touch ~27 pass call sites for a Low advisory finding, and the faithfulness
/// value lives in the centralization + the honest "default operator opinion"
/// labeling, not in a config knob no operator has yet asked to turn. Promote it
/// when an operator needs a non-default advisory profile.
[<RequireQualifiedAccess>]
module AdvisoryTuning =

    /// SchemaComplexity composite-score weight vector + per-metric
    /// normalization caps. The weights are a relative emphasis (they need not
    /// sum to 1); each cap is a "typical large schema" reference point that
    /// bounds the raw metric to [0, 1] before the weighted blend.
    type SchemaComplexityTuning =
        {
            WeightCyclomatic  : decimal
            WeightCoupling    : decimal
            WeightCohesion    : decimal
            WeightDepth       : decimal
            WeightNullability : decimal
            CapCyclomatic     : decimal
            CapCoupling       : decimal
            CapDepth          : decimal
            CapNullability    : decimal
        }

    /// CentralityPass personalized-PageRank tuning.
    type CentralityTuning =
        {
            DampingFactor  : decimal
            ConvergenceEps : decimal
            MaxIterations  : int
        }

    /// BoundedContextPass community-detection tuning.
    type BoundedContextTuning =
        {
            /// Label-propagation convergence cap — the max rounds before the
            /// fixed-point loop bails (recon #18 — the lone holdout that
            /// hardcoded its loop bound while every sibling read from here).
            MaxPropagationRounds : int
        }

    /// The aggregate advisory-tuning profile.
    type T =
        {
            SchemaComplexity : SchemaComplexityTuning
            /// ProfileAnomaly: a column flags as anomalous past mean ± Nσ.
            ProfileAnomalySigma : decimal
            Centrality : CentralityTuning
            BoundedContext : BoundedContextTuning
        }

    /// The production defaults — byte-identical to the per-pass constants they
    /// replace (SchemaComplexity weights `[0.20, 0.20, 0.15, 0.25, 0.20]` + caps
    /// `[500, 5, 20, 1]`; ProfileAnomaly `2σ`; Centrality PageRank
    /// `0.85 / 1e-6 / 100`), so no advisory output moves.
    let defaults : T =
        {
            SchemaComplexity =
                {
                    WeightCyclomatic  = 0.20m
                    WeightCoupling    = 0.20m
                    WeightCohesion    = 0.15m
                    WeightDepth       = 0.25m
                    WeightNullability = 0.20m
                    CapCyclomatic     = 500.0m
                    CapCoupling       = 5.0m
                    CapDepth          = 20.0m
                    CapNullability    = 1.0m
                }
            ProfileAnomalySigma = 2.0m
            Centrality =
                {
                    DampingFactor  = 0.85m
                    ConvergenceEps = 0.000001m
                    MaxIterations  = 100
                }
            BoundedContext =
                {
                    MaxPropagationRounds = 50
                }
        }
