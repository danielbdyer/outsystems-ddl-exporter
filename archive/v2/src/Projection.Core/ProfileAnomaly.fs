namespace Projection.Core

/// Profile anomaly report (H-073). Captures attributes whose null
/// rate or coefficient of variation falls more than 2σ above the
/// per-table mean — statistical outliers that warrant operator review.
type ProfileAnomalyReport = {
    /// Attributes whose null rate exceeds (table mean + 2σ).
    /// Each pair is (attributeKey, nullRate). Sorted by SsKey.
    HighNullRateColumns : (SsKey * decimal) list
    /// Attributes whose coefficient of variation exceeds
    /// (table mean CV + 2σ). Each pair is (attributeKey, CV).
    /// Sorted by SsKey.
    HighCvColumns       : (SsKey * decimal) list
}
