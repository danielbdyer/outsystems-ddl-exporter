using System;
using System.Collections.Immutable;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.Profiling;

public interface ISqlProfilerPreflight
{
    Result<SqlProfilerPreflightResult> Run(SqlProfilerPreflightRequest request);
}

public sealed record SqlProfilerPreflightRequest(
    string ConnectionString,
    SqlConnectionOptions ConnectionOptions,
    SqlProfilerOptions ProfilerOptions,
    bool SkipConnectionTest = false);

public sealed record SqlProfilerPreflightResult(ImmutableArray<SqlProfilerPreflightDiagnostic> Diagnostics)
{
    public static SqlProfilerPreflightResult Empty { get; } = new(ImmutableArray<SqlProfilerPreflightDiagnostic>.Empty);
}

public sealed record SqlProfilerPreflightDiagnostic(SqlProfilerPreflightSeverity Severity, string Message);

public enum SqlProfilerPreflightSeverity
{
    Information,
    Warning
}

public sealed class SqlProfilerPreflight : ISqlProfilerPreflight
{
    private readonly Func<string, SqlConnectionOptions, IDbConnectionFactory> _connectionFactoryFactory;

    public SqlProfilerPreflight(Func<string, SqlConnectionOptions, IDbConnectionFactory> connectionFactoryFactory)
    {
        _connectionFactoryFactory = connectionFactoryFactory ?? throw new ArgumentNullException(nameof(connectionFactoryFactory));
    }

    public Result<SqlProfilerPreflightResult> Run(SqlProfilerPreflightRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.ConnectionString))
        {
            return ValidationError.Create(
                "pipeline.sqlProfiler.connectionString.missing",
                "Connection string is required when running the SQL profiler preflight.");
        }

        var diagnostics = ImmutableArray.CreateBuilder<SqlProfilerPreflightDiagnostic>();
        var sampling = request.ProfilerOptions?.Sampling ?? SqlProfilerOptions.Default.Sampling;

        if (sampling.RowCountSamplingThreshold <= 0)
        {
            return ValidationError.Create(
                "pipeline.sqlProfiler.sampling.threshold.invalid",
                "Row sampling threshold must be greater than zero.");
        }

        if (sampling.SampleSize <= 0)
        {
            return ValidationError.Create(
                "pipeline.sqlProfiler.sampling.sampleSize.invalid",
                "Sample size must be greater than zero.");
        }

        if (sampling.SampleSize > sampling.RowCountSamplingThreshold)
        {
            diagnostics.Add(new SqlProfilerPreflightDiagnostic(
                SqlProfilerPreflightSeverity.Warning,
                $"Sample size {sampling.SampleSize.ToString("N0", CultureInfo.InvariantCulture)} exceeds row sampling threshold {sampling.RowCountSamplingThreshold.ToString("N0", CultureInfo.InvariantCulture)}. The profiler will clamp the sample to the threshold. Consider increasing --sql-sample-threshold."));
        }

        var limits = request.ProfilerOptions?.Limits ?? SqlProfilerOptions.Default.Limits;
        if (limits.MaxRowsPerTable.HasValue && limits.MaxRowsPerTable.Value < sampling.RowCountSamplingThreshold)
        {
            diagnostics.Add(new SqlProfilerPreflightDiagnostic(
                SqlProfilerPreflightSeverity.Information,
                $"Max rows per table {limits.MaxRowsPerTable.Value.ToString("N0", CultureInfo.InvariantCulture)} is lower than the row sampling threshold {sampling.RowCountSamplingThreshold.ToString("N0", CultureInfo.InvariantCulture)}. The profiler will use the lower limit."));
        }

        if (!request.SkipConnectionTest)
        {
            try
            {
                var factory = _connectionFactoryFactory(request.ConnectionString, request.ConnectionOptions);
                using var connection = factory.CreateOpenConnectionAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException or ArgumentException)
            {
                return ValidationError.Create(
                    "pipeline.sqlProfiler.connection.failed",
                    $"Failed to open SQL connection using the provided options: {ex.Message}");
            }
        }

        return new SqlProfilerPreflightResult(diagnostics.ToImmutable());
    }
}
