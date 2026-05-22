namespace Projection.Core

/// Query plan hint suggestions derived from FK selectivity profiling
/// (H-076). Operators consume these suggestions via the
/// `v2 suggest-config` CLI to tune index fill factors on highly
/// selective FK indexes.
type QueryHintReport = {
    /// Pairs of (indexSsKey, suggestedFillFactor). Sorted by SsKey
    /// for T1 byte-determinism.
    FillFactorSuggestions : (SsKey * int) list
}
