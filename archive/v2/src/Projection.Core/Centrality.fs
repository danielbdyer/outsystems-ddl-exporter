namespace Projection.Core

/// Per-kind PageRank score (H-071). The score is a personalized
/// PageRank value computed over the FK adjacency graph; higher scores
/// indicate structurally central kinds that many other kinds depend on.
type CentralityScore = {
    SsKey  : SsKey
    Score  : decimal
}

/// Full centrality ranking result from one PageRank run (H-071).
type CentralityRanking = {
    /// Per-kind scores. Sorted Score DESC, SsKey ASC for ties.
    Scores     : CentralityScore list
    /// Power-iteration steps taken until convergence (or the
    /// configured maximum iteration cap).
    Iterations : int
}
