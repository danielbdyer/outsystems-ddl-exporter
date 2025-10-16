using System;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.SqlExtraction;

namespace Osm.Pipeline.Sql;

public static class ResolvedSqlOptionsExtensions
{
    public static SqlSamplingOptions ToSamplingOptions(this ResolvedSqlOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        return CreateSamplingOptions(options.Sampling);
    }

    public static SqlConnectionOptions ToConnectionOptions(this ResolvedSqlOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        return CreateConnectionOptions(options.Authentication);
    }

    public static SqlExecutionOptions ToExecutionOptions(this ResolvedSqlOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        return new SqlExecutionOptions(options.CommandTimeoutSeconds, options.ToSamplingOptions());
    }

    private static SqlSamplingOptions CreateSamplingOptions(SqlSamplingSettings? settings)
    {
        var threshold = settings?.RowSamplingThreshold ?? SqlSamplingOptions.Default.RowCountSamplingThreshold;
        var sampleSize = settings?.SampleSize ?? SqlSamplingOptions.Default.SampleSize;
        return new SqlSamplingOptions(threshold, sampleSize);
    }

    private static SqlConnectionOptions CreateConnectionOptions(SqlAuthenticationSettings? settings)
    {
        return new SqlConnectionOptions(
            settings?.Method,
            settings?.TrustServerCertificate,
            settings?.ApplicationName,
            settings?.AccessToken);
    }
}
