namespace Projection.Core

/// Composite schema complexity metrics (H-075). Each field measures
/// a structural quality of the schema's FK graph and attribute
/// composition; `OverallScore` is a weighted aggregate normalized
/// to [0, 1] where 1 represents maximum complexity.
type SchemaComplexity = {
    /// Number of FK edges in the graph (McCabe-style independent
    /// dependency-path count).
    CyclomaticComplexity : int
    /// Average FK references per kind (decimal).
    CouplingIndex        : decimal
    /// Mean intra-module edge fraction across modules (1.0 = fully
    /// cohesive; 0.0 = all cross-module FK references).
    CohesionIndex        : decimal
    /// Number of Kahn dependency depth layers minus 1; measures the
    /// longest FK dependency chain in the schema.
    DepthOfInheritance   : int
    /// Fraction of all attributes that are nullable across the
    /// catalog (0.0 = none nullable; 1.0 = all nullable).
    NullabilityRatio     : decimal
    /// Weighted composite score, normalized to [0, 1].
    OverallScore         : decimal
}
