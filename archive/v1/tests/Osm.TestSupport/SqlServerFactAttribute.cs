using System;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Osm.TestSupport;

/// <summary>
/// xUnit Fact attribute that skips the test if SQL Server is not available.
/// Tests marked with this attribute will only run if a local SQL Server instance can be connected to.
/// </summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    private static readonly Lazy<bool> IsSqlServerAvailable = new(CheckSqlServerAvailability);

    public SqlServerFactAttribute()
    {
        if (!IsSqlServerAvailable.Value)
        {
            Skip = "SQL Server is not available. Ensure SQL Server is running locally or via Docker.";
        }
    }

    private static bool CheckSqlServerAvailability()
    {
        try
        {
            // Try to connect to local SQL Server instance with trusted connection
            using var connection = new SqlConnection("Server=.;Database=master;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=2");
            connection.Open();
            return true;
        }
        catch
        {
            // SQL Server not available
            return false;
        }
    }
}
