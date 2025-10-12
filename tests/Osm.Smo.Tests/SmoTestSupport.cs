using System;
using System.Threading;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using Osm.Smo;
using Xunit;

namespace Osm.Smo.Tests;

internal static class SmoTestSupport
{
    private const string SkipMessage = "SMO tests require a local SQL Server instance.";
    private static readonly Lazy<bool> SqlServerAvailable = new(ProbeSqlServerAvailability, LazyThreadSafetyMode.ExecutionAndPublication);

    public static void SkipUnlessSqlServerAvailable()
    {
        Skip.IfNot(SqlServerAvailable.Value, SkipMessage);
    }

    public static void SkipOnConnectionFailure(Exception exception)
    {
        if (IsConnectionFailure(exception))
        {
            Skip.IfNot(false, SkipMessage);
        }
    }

    private static bool ProbeSqlServerAvailability()
    {
        try
        {
            using var context = new SmoContext();
            _ = context.Database.Name;
            return true;
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            return false;
        }
    }

    private static bool IsConnectionFailure(Exception exception)
    {
        return exception switch
        {
            ConnectionFailureException => true,
            SqlException => true,
            FailedOperationException { InnerException: { } inner } => IsConnectionFailure(inner),
            _ when exception.InnerException is not null => IsConnectionFailure(exception.InnerException),
            _ => false,
        };
    }
}
