using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Osm.Domain.Profiling;

namespace Osm.Pipeline.Profiling;

internal interface IProfilingProbePolicy
{
    Task<ProfilingProbeResult<T>> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        T fallback,
        long sampleSize,
        CancellationTokenSource? tableCancellation,
        CancellationToken originalToken);
}

internal sealed class ProfilingProbePolicy : IProfilingProbePolicy
{
    private readonly TimeProvider _timeProvider;

    public ProfilingProbePolicy(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<ProfilingProbeResult<T>> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        T fallback,
        long sampleSize,
        CancellationTokenSource? tableCancellation,
        CancellationToken originalToken)
    {
        try
        {
            var token = tableCancellation?.Token ?? originalToken;
            var result = await operation(token).ConfigureAwait(false);
            return new ProfilingProbeResult<T>(result, ProfilingProbeStatus.CreateSucceeded(_timeProvider.GetUtcNow(), sampleSize));
        }
        catch (OperationCanceledException) when (IsTableTimeout(tableCancellation, originalToken))
        {
            return new ProfilingProbeResult<T>(fallback, ProfilingProbeStatus.CreateCancelled(_timeProvider.GetUtcNow(), sampleSize));
        }
        catch (DbException ex) when (IsTimeoutException(ex))
        {
            return new ProfilingProbeResult<T>(fallback, ProfilingProbeStatus.CreateFallbackTimeout(_timeProvider.GetUtcNow(), sampleSize));
        }
    }

    internal static bool IsTableTimeout(CancellationTokenSource? tableCancellation, CancellationToken originalToken)
    {
        return tableCancellation is not null && tableCancellation.IsCancellationRequested && !originalToken.IsCancellationRequested;
    }

    internal static bool IsTimeoutException(DbException exception)
    {
        if (exception is SqlException sqlException)
        {
            foreach (SqlError error in sqlException.Errors)
            {
                if (error.Number == -2)
                {
                    return true;
                }
            }
        }

        return exception.ErrorCode == -2;
    }
}
