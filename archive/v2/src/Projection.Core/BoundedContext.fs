namespace Projection.Core

/// One candidate bounded context (H-072) identified by label-
/// propagation community detection over the undirected FK coupling
/// graph. A high InternalEdgeCount / ExternalEdgeCount ratio
/// indicates strong internal cohesion.
type BoundedContextCandidate = {
    /// Kind with the highest total FK degree within the community;
    /// serves as the representative name for this context boundary.
    AnchorKey         : SsKey
    /// All kind keys in this community, sorted by SsKey.
    Members           : SsKey list
    /// FK edges where both endpoints are within this community.
    InternalEdgeCount : int
    /// FK edges where exactly one endpoint is in this community.
    ExternalEdgeCount : int
}

/// Bounded context discovery result (H-072).
type BoundedContextDiscovery = {
    Candidates : BoundedContextCandidate list
}
