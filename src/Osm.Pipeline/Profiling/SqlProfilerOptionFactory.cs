using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Sql;
using Osm.Smo;

namespace Osm.Pipeline.Profiling;

internal static class SqlProfilerOptionFactory
{
    public static SqlSamplingOptions CreateSamplingOptions(SqlSamplingSettings configuration)
    {
        var threshold = configuration.RowSamplingThreshold ?? SqlSamplingOptions.Default.RowCountSamplingThreshold;
        var sampleSize = configuration.SampleSize ?? SqlSamplingOptions.Default.SampleSize;
        return new SqlSamplingOptions(threshold, sampleSize);
    }

    public static SqlConnectionOptions CreateConnectionOptions(SqlAuthenticationSettings configuration)
    {
        return new SqlConnectionOptions(
            configuration.Method,
            configuration.TrustServerCertificate,
            configuration.ApplicationName,
            configuration.AccessToken);
    }

    public static SqlProfilerOptions CreateProfilerOptions(ResolvedSqlOptions sqlOptions, SmoBuildOptions smoOptions)
    {
        return SqlProfilerOptions.Default with
        {
            CommandTimeoutSeconds = sqlOptions.CommandTimeoutSeconds,
            Sampling = CreateSamplingOptions(sqlOptions.Sampling),
            NamingOverrides = smoOptions.NamingOverrides
        };
    }
}
