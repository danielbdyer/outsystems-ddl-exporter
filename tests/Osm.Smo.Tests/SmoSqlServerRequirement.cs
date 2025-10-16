using System;
using System.Threading;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using Osm.Smo;

namespace Osm.Smo.Tests;

internal static class SmoSqlServerRequirement
{
    private static readonly Lazy<(bool Available, string? Reason)> Availability =
        new(EvaluateAvailability, LazyThreadSafetyMode.ExecutionAndPublication);

    public static bool IsUnavailable(Exception? exception)
    {
        return exception switch
        {
            null => false,
            ConnectionFailureException => true,
            SqlException => true,
            FailedOperationException failed when IsUnavailable(failed.InnerException) => true,
            _ when IsUnavailable(exception.InnerException) => true,
            _ => false
        };
    }

    public static string BuildReason(Exception exception)
    {
        var current = exception;
        while (current.InnerException is not null)
        {
            current = current.InnerException;
        }

        return current.Message;
    }

    public static bool IsAvailable(out string? reason)
    {
        var (available, message) = Availability.Value;
        reason = message;
        return available;
    }

    private static (bool Available, string? Reason) EvaluateAvailability()
    {
        try
        {
            using var factory = new SmoObjectGraphFactory();
            return (true, null);
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            return (false, BuildReason(ex));
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
