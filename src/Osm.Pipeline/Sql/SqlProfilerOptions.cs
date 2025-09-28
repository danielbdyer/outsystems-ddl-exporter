namespace Osm.Pipeline.Sql;

public sealed record SqlProfilerOptions(int? CommandTimeoutSeconds, SqlSamplingOptions Sampling)
{
    public static SqlProfilerOptions Default { get; } = new(null, SqlSamplingOptions.Default);
}
