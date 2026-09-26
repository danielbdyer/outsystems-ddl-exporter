namespace Osm.Validation.Tightening;

internal interface ITighteningAnalyzer
{
    void Analyze(EntityContext context, ColumnAnalysisBuilder builder);
}
