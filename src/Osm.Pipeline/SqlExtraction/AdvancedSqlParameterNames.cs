namespace Osm.Pipeline.SqlExtraction;

internal static class AdvancedSqlParameterNames
{
    public const string ModuleNamesCsv = "@ModuleNamesCsv";
    public const string IncludeSystem = "@IncludeSystem";
    public const string IncludeInactive = "@IncludeInactive";
    public const string OnlyActiveAttributes = "@OnlyActiveAttributes";
    public const string RowSamplingThreshold = "@RowSamplingThreshold";
    public const string SampleSize = "@SampleSize";
}
