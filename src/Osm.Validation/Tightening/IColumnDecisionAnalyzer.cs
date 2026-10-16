namespace Osm.Validation.Tightening;

/// <summary>
/// Analyzes a single column in context and writes its tightening decision into the
/// supplied <see cref="ColumnAnalysisBuilder"/>. Distinct from the opportunity-facing
/// <c>Opportunities.ITighteningAnalyzer</c> (which produces findings from finished
/// decisions); they previously shared a name.
/// </summary>
internal interface IColumnDecisionAnalyzer
{
    void Analyze(EntityContext context, ColumnAnalysisBuilder builder);
}
